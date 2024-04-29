'use strict';

const { Topics, EventTypes } = require('./events');
const { canTransition, courierStatuses, onTheWay, active, isTerminal } = require('./stateMachine');
const { appendEvent } = require('./eventStore');
const { project } = require('./projection');
const { dayOf } = require('./readModels');

class HttpError extends Error {
  constructor(statusCode, title, detail) {
    super(detail ?? title);
    this.statusCode = statusCode;
    this.title = title;
    this.detail = detail;
  }
}

const fuzz = (value) => Math.round(value * 100) / 100;

class TrackingService {
  constructor({ repo, locations, etaCache, board, publisher, config, log, now = () => new Date() }) {
    this.repo = repo;
    this.locations = locations;
    this.etaCache = etaCache;
    this.board = board;
    this.publisher = publisher;
    this.config = config;
    this.log = log;
    this.now = now;
  }

  async recordPing(courierId, { lat, lng, speedKmh, recordedAt }) {
    const ping = { courierId, lat, lng, speedKmh: speedKmh ?? null, recordedAt: recordedAt ? new Date(recordedAt) : this.now() };
    await this.locations.record(ping);

    const shipments = await this.repo.activeForCourier(courierId);
    await this.publisher.publish(Topics.LocationUpdated, courierId, EventTypes.LocationUpdated, {
      courierId,
      lat,
      lng,
      speedKmh: ping.speedKmh,
      recordedAt: ping.recordedAt.toISOString(),
      shipments: shipments.map((s) => ({
        shipmentId: s.shipmentId,
        trackingNumber: s.trackingNumber,
        status: s.status,
        serviceLevel: s.serviceLevel,
        pickup: { lat: s.pickup.lat, lng: s.pickup.lng },
        destination: { lat: s.dropoff.lat, lng: s.dropoff.lng },
      })),
    });
    return { accepted: true, activeShipments: shipments.map((s) => s.trackingNumber) };
  }

  async updateStatus(shipmentId, user, { status, note, proof, lat, lng }) {
    if (!courierStatuses.includes(status)) {
      throw new HttpError(400, 'Invalid status', `Couriers can report ${courierStatuses.join(', ')}.`);
    }
    if (status === 'Delivered' && (!proof?.recipientName || !proof?.signatureName)) {
      throw new HttpError(400, 'Proof of delivery required', 'A delivery needs recipientName and signatureName.');
    }
    if (status === 'Failed' && !note) {
      throw new HttpError(400, 'Reason required', 'Say why the delivery failed in "note".');
    }

    const result = await this.repo.transaction(async (tx) => {
      const view = await tx.lockView(shipmentId);
      if (!view) throw new HttpError(404, 'Shipment not found');
      if (user.role === 'Courier' && view.courierId !== user.courierId) {
        throw new HttpError(403, 'Not your shipment', 'This shipment is assigned to another courier.');
      }
      if (view.status === status) return { view, changed: false };
      if (!canTransition(view.status, status)) {
        throw new HttpError(409, 'Invalid transition', `A ${view.status} shipment can't become ${status}.`);
      }

      const occurredAt = this.now();
      const proofOfDelivery =
        status === 'Delivered'
          ? { recipientName: proof.recipientName, signatureName: proof.signatureName, photoUrl: proof.photoUrl ?? null, deliveredAt: occurredAt.toISOString() }
          : null;
      const data = {
        note: note ?? null,
        proof: proofOfDelivery ?? undefined,
        location: lat !== undefined && lng !== undefined ? { lat, lng } : undefined,
        by: user.role,
      };

      const appended = await appendEvent(tx, { shipmentId, type: status, data, source: user.role.toLowerCase(), occurredAt });
      if (!appended?.previous || appended.previous.status !== view.status) {
        throw new HttpError(409, 'Concurrent update', 'The shipment changed while updating, try again.');
      }
      await this.publisher.publish(Topics.StatusChanged, shipmentId, EventTypes.StatusChanged, {
        shipmentId,
        trackingNumber: view.trackingNumber,
        courierId: view.courierId,
        status,
        note: note ?? null,
        proofOfDelivery,
        occurredAt: occurredAt.toISOString(),
      });
      return { view: appended.next, changed: true, appended };
    });

    if (result.changed) {
      await this.updateReadModels(result.appended);
      this.log?.info({ trackingNumber: result.view.trackingNumber, status }, 'status updated');
    }
    return { shipment: result.view, changed: result.changed };
  }

  async publicView(trackingNumber) {
    const shipment = await this.repo.getViewByTrackingNumber(trackingNumber);
    if (!shipment) return null;

    const [events, courierLocation, cachedEta] = await Promise.all([
      this.repo.events(shipment.shipmentId),
      shipment.courierId && onTheWay.includes(shipment.status) ? this.locations.get(shipment.courierId) : null,
      isTerminal(shipment.status) ? null : this.etaCache.get(shipment.shipmentId),
    ]);

    const eta = cachedEta;

    return {
      trackingNumber: shipment.trackingNumber,
      status: shipment.status,
      serviceLevel: shipment.serviceLevel,
      from: shipment.pickup.city,
      to: { city: shipment.dropoff.city, postalCode: shipment.dropoff.postalCode, lat: fuzz(shipment.dropoff.lat), lng: fuzz(shipment.dropoff.lng) },
      courierName: shipment.courierName ? shipment.courierName.split(' ')[0] : null,
      slaDueAt: shipment.slaDueAt,
      eta: isTerminal(shipment.status) ? null : eta,
      isDelayed: !isTerminal(shipment.status) && (new Date(shipment.slaDueAt) < this.now() || Boolean(eta && new Date(eta.etaAt) > new Date(shipment.slaDueAt))),
      lastLocation: courierLocation ? { lat: courierLocation.lat, lng: courierLocation.lng, recordedAt: courierLocation.recordedAt } : null,
      proofOfDelivery: shipment.deliveredTo ? { recipientName: shipment.deliveredTo, deliveredAt: shipment.deliveredAt, hasPhoto: Boolean(lastProof(events)?.photoUrl) } : null,
      timeline: events.map(timelineEntry),
    };
  }

  async timeline(shipmentId) {
    return (await this.repo.events(shipmentId)).map((e) => ({ ...timelineEntry(e), source: e.source, proof: e.data?.proof }));
  }

  boardView() {
    return this.board.read(dayOf(this.now()), this.locations);
  }

  // kafka handlers

  async onShipmentCreated(eventId, e) {
    const data = {
      trackingNumber: e.trackingNumber,
      customerId: e.customerId,
      serviceLevel: e.serviceLevel,
      pickup: { city: e.pickup.city, lat: e.pickup.lat, lng: e.pickup.lng },
      dropoff: { city: e.dropoff.city, postalCode: e.dropoff.postalCode, lat: e.dropoff.lat, lng: e.dropoff.lng },
      weightKg: e.weightKg,
      slaDueAt: e.slaDueAt,
      note: `Booked ${e.serviceLevel}`,
    };
    await this.appendFromEvent(eventId, e.shipmentId, 'Created', data, 'shipments', e.createdAt, (view) => !view);
    if (e.etaAt) await this.etaCache.put(e.shipmentId, { etaAt: e.etaAt, calculatedAt: e.createdAt });
  }

  async onShipmentCancelled(eventId, e) {
    await this.appendFromEvent(eventId, e.shipmentId, 'Cancelled', { note: e.reason }, 'shipments', e.cancelledAt, (view) => !isTerminal(view.status));
  }

  async onCourierAssigned(eventId, e) {
    const firstName = e.courierName.split(' ')[0];
    await this.appendFromEvent(eventId, e.shipmentId, 'Assigned', { courierId: e.courierId, courierName: e.courierName, mode: e.mode, note: `Courier ${firstName} assigned` },
      'dispatch', e.assignedAt, (view) => ['Created', 'Assigned'].includes(view.status) && view.courierId !== e.courierId);
  }

  async onEtaRecalculated(_eventId, e) {
    await this.etaCache.put(e.shipmentId, e);
  }

  async appendFromEvent(eventId, shipmentId, type, data, source, occurredAt, when) {
    const appended = await this.repo.transaction(async (tx) => {
      const view = await tx.lockView(shipmentId);
      // not created yet - throw so it gets retried
      if (!view && type !== 'Created') throw new Error(`unknown shipment ${shipmentId}`);
      if (!when(view)) return null;
      return appendEvent(tx, { shipmentId, type, data, source, sourceEventId: eventId, occurredAt });
    });
    if (appended) await this.updateReadModels(appended);
  }

  async updateReadModels(appended) {
    try {
      await this.board.apply(appended);
    } catch (err) {
      // npm run rebuild fixes it
      this.log?.warn({ err: err.message }, 'dispatcher board update failed');
    }
  }

  async rebuildProjections() {
    const ids = await this.repo.shipmentIds();
    for (const id of ids) {
      const state = project(await this.repo.events(id));
      if (state) await this.repo.saveView(state);
    }
    const since = new Date(this.now().getTime() - 3 * 24 * 60 * 60 * 1000);
    await this.board.rebuild(await this.repo.allViews(), await this.repo.eventsSince(since));
    return { shipments: ids.length };
  }
}

const timelineEntry = (e) => ({ status: e.type, note: e.data?.note ?? null, occurredAt: e.occurredAt });
const lastProof = (events) => [...events].reverse().find((e) => e.data?.proof)?.data.proof;

module.exports = { TrackingService, HttpError, active };
