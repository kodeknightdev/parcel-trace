'use strict';

const { active } = require('./stateMachine');

const toView = (r) =>
  r && {
    shipmentId: r.shipment_id,
    trackingNumber: r.tracking_number,
    customerId: r.customer_id,
    serviceLevel: r.service_level,
    status: r.status,
    courierId: r.courier_id,
    courierName: r.courier_name,
    pickup: { city: r.pickup_city, lat: r.pickup_lat, lng: r.pickup_lng },
    dropoff: { city: r.dropoff_city, postalCode: r.dropoff_postal, lat: r.dropoff_lat, lng: r.dropoff_lng },
    weightKg: Number(r.weight_kg),
    slaDueAt: r.sla_due_at,
    createdAt: r.created_at,
    deliveredTo: r.delivered_to,
    deliveredAt: r.delivered_at,
    lastSequence: r.last_sequence,
    updatedAt: r.updated_at,
  };

const toEvent = (r) => ({
  shipmentId: r.shipment_id,
  sequence: r.sequence,
  type: r.type,
  data: r.data,
  source: r.source,
  sourceEventId: r.source_event_id,
  occurredAt: r.occurred_at,
});

class TrackingRepository {
  constructor(queryable) {
    this.db = queryable;
  }

  async transaction(fn) {
    const client = await this.db.connect();
    try {
      await client.query('BEGIN');
      const result = await fn(new TrackingRepository(client));
      await client.query('COMMIT');
      return result;
    } catch (err) {
      await client.query('ROLLBACK').catch(() => {});
      throw err;
    } finally {
      client.release();
    }
  }

  // event log

  async insertEvent(e) {
    const { rows } = await this.db.query(
      `INSERT INTO shipment_events (shipment_id, sequence, type, data, source, source_event_id, occurred_at)
       VALUES ($1,$2,$3,$4,$5,$6,$7)
       ON CONFLICT (source_event_id) DO NOTHING
       RETURNING *`,
      [e.shipmentId, e.sequence, e.type, JSON.stringify(e.data ?? {}), e.source, e.sourceEventId ?? null, e.occurredAt],
    );
    return rows[0] ? toEvent(rows[0]) : null;
  }

  async events(shipmentId) {
    const { rows } = await this.db.query('SELECT * FROM shipment_events WHERE shipment_id = $1 ORDER BY sequence', [shipmentId]);
    return rows.map(toEvent);
  }

  async eventsSince(since) {
    const { rows } = await this.db.query('SELECT * FROM shipment_events WHERE occurred_at >= $1 ORDER BY shipment_id, sequence', [since]);
    return rows.map(toEvent);
  }

  async shipmentIds() {
    const { rows } = await this.db.query('SELECT DISTINCT shipment_id FROM shipment_events');
    return rows.map((r) => r.shipment_id);
  }

  // projection

  async lockView(shipmentId) {
    const { rows } = await this.db.query('SELECT * FROM shipment_view WHERE shipment_id = $1 FOR UPDATE', [shipmentId]);
    return toView(rows[0]);
  }

  async getView(shipmentId) {
    const { rows } = await this.db.query('SELECT * FROM shipment_view WHERE shipment_id = $1', [shipmentId]);
    return toView(rows[0]);
  }

  async getViewByTrackingNumber(trackingNumber) {
    const { rows } = await this.db.query('SELECT * FROM shipment_view WHERE tracking_number = $1', [trackingNumber]);
    return toView(rows[0]);
  }

  async saveView(v) {
    await this.db.query(
      `INSERT INTO shipment_view (shipment_id, tracking_number, customer_id, service_level, status, courier_id, courier_name,
         pickup_city, pickup_lat, pickup_lng, dropoff_city, dropoff_postal, dropoff_lat, dropoff_lng, weight_kg, sla_due_at,
         created_at, delivered_to, delivered_at, last_sequence, updated_at)
       VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21)
       ON CONFLICT (shipment_id) DO UPDATE SET status = EXCLUDED.status, courier_id = EXCLUDED.courier_id,
         courier_name = EXCLUDED.courier_name, delivered_to = EXCLUDED.delivered_to, delivered_at = EXCLUDED.delivered_at,
         last_sequence = EXCLUDED.last_sequence, updated_at = EXCLUDED.updated_at`,
      [v.shipmentId, v.trackingNumber, v.customerId, v.serviceLevel, v.status, v.courierId, v.courierName,
        v.pickup.city, v.pickup.lat, v.pickup.lng, v.dropoff.city, v.dropoff.postalCode, v.dropoff.lat, v.dropoff.lng,
        v.weightKg, v.slaDueAt, v.createdAt, v.deliveredTo, v.deliveredAt, v.lastSequence, v.updatedAt],
    );
  }

  async activeForCourier(courierId) {
    const { rows } = await this.db.query(
      'SELECT * FROM shipment_view WHERE courier_id = $1 AND status = ANY($2) ORDER BY created_at',
      [courierId, active],
    );
    return rows.map(toView);
  }

  async allViews() {
    const { rows } = await this.db.query('SELECT * FROM shipment_view');
    return rows.map(toView);
  }

  // eta

  async getEta(shipmentId) {
    const { rows } = await this.db.query('SELECT * FROM eta_estimates WHERE shipment_id = $1', [shipmentId]);
    const r = rows[0];
    return r && { etaAt: r.eta_at, minutesRemaining: r.minutes_remaining, distanceKm: r.distance_km, calculatedAt: r.calculated_at };
  }

  async saveEta(shipmentId, eta) {
    const { rowCount } = await this.db.query(
      `INSERT INTO eta_estimates (shipment_id, eta_at, minutes_remaining, distance_km, calculated_at)
       VALUES ($1,$2,$3,$4,$5)
       ON CONFLICT (shipment_id) DO UPDATE SET eta_at = EXCLUDED.eta_at, minutes_remaining = EXCLUDED.minutes_remaining,
         distance_km = EXCLUDED.distance_km, calculated_at = EXCLUDED.calculated_at
       WHERE eta_estimates.calculated_at < EXCLUDED.calculated_at`,
      [shipmentId, eta.etaAt, eta.minutesRemaining ?? null, eta.distanceKm ?? null, eta.calculatedAt],
    );
    return rowCount === 1;
  }

  // pings

  async addPing(p) {
    await this.db.query(
      'INSERT INTO location_pings (courier_id, lat, lng, speed_kmh, recorded_at) VALUES ($1,$2,$3,$4,$5)',
      [p.courierId, p.lat, p.lng, p.speedKmh ?? null, p.recordedAt],
    );
  }

  async latestPing(courierId) {
    const { rows } = await this.db.query(
      'SELECT lat, lng, speed_kmh, recorded_at FROM location_pings WHERE courier_id = $1 ORDER BY recorded_at DESC LIMIT 1',
      [courierId],
    );
    const r = rows[0];
    return r && { courierId, lat: r.lat, lng: r.lng, speedKmh: r.speed_kmh, recordedAt: r.recorded_at };
  }

  async pingHistory(courierId, limit = 100) {
    const { rows } = await this.db.query(
      'SELECT lat, lng, speed_kmh, recorded_at FROM location_pings WHERE courier_id = $1 ORDER BY recorded_at DESC LIMIT $2',
      [courierId, limit],
    );
    return rows.map((r) => ({ lat: r.lat, lng: r.lng, speedKmh: r.speed_kmh, recordedAt: r.recorded_at }));
  }

  // processed events

  async isProcessed(eventId, consumer) {
    const { rowCount } = await this.db.query('SELECT 1 FROM processed_events WHERE event_id = $1 AND consumer = $2', [eventId, consumer]);
    return rowCount > 0;
  }

  async markProcessed(eventId, consumer) {
    await this.db.query('INSERT INTO processed_events (event_id, consumer) VALUES ($1, $2) ON CONFLICT DO NOTHING', [eventId, consumer]);
  }

  async ping() {
    await this.db.query('SELECT 1');
  }
}

module.exports = { TrackingRepository };
