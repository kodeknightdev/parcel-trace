import { beforeEach, describe, expect, it } from 'vitest';
import { createHarness } from './setup';
import { SHIPMENT_ID, COURIER_ID, createdEvent, assignedEvent } from './fakes';

const courier = { id: 'u1', role: 'Courier', courierId: COURIER_ID };
const NOW = new Date('2024-04-22T12:00:00Z');

describe('TrackingService', () => {
  let h;

  beforeEach(async () => {
    h = createHarness({ now: () => NOW, etaResult: { distanceKm: 4.2, minutes: 17, etaAt: '2024-04-22T12:17:00Z', trafficFactor: 1.2 } });
    await h.handlers['shipments.created']({ eventId: 'e-created', payload: createdEvent() });
  });

  const assign = (eventId = 'e-assigned', overrides = {}) => h.handlers['dispatch.courier-assigned']({ eventId, payload: assignedEvent(overrides) });

  it('appends one event per fact and projects the current state, ignoring redeliveries', async () => {
    await assign();
    await assign();
    await h.handlers['shipments.created']({ eventId: 'e-created', payload: createdEvent() });

    const log = await h.repo.events(SHIPMENT_ID);
    expect(log.map((e) => [e.sequence, e.type])).toEqual([[1, 'Created'], [2, 'Assigned']]);
    expect(await h.repo.getView(SHIPMENT_ID)).toMatchObject({ status: 'Assigned', courierId: COURIER_ID, lastSequence: 2 });
  });

  it('also dedupes by the log\'s unique source event id when the processed table misses it', async () => {
    await assign();
    h.repo.processed.clear();
    await assign();
    expect((await h.repo.events(SHIPMENT_ID)).length).toBe(2);
  });

  it('throws on an assignment for a shipment it has not seen yet so the message is retried', async () => {
    await expect(h.handlers['dispatch.courier-assigned']({ eventId: 'x', payload: assignedEvent({ shipmentId: 'unknown' }) })).rejects.toThrow(/unknown shipment/);
  });

  it('publishes location-updated with the courier\'s active shipments on a ping', async () => {
    await assign();
    const result = await h.service.recordPing(COURIER_ID, { lat: 52.51, lng: 13.4 });

    expect(result.activeShipments).toEqual(['PT-2024-ABC234']);
    const [message] = h.publisher.messages;
    expect(message.topic).toBe('tracking.location-updated');
    expect(message.payload.shipments[0]).toMatchObject({ trackingNumber: 'PT-2024-ABC234', status: 'Assigned', destination: { lat: 52.503, lng: 13.332 } });
  });

  it('walks a parcel through pick-up to delivery and publishes each status', async () => {
    await assign();
    for (const status of ['PickedUp', 'InTransit', 'OutForDelivery']) {
      await h.service.updateStatus(SHIPMENT_ID, courier, { status });
    }
    await h.service.updateStatus(SHIPMENT_ID, courier, { status: 'Delivered', proof: { recipientName: 'Jonas Weber', signatureName: 'J. Weber' } });

    expect((await h.repo.getView(SHIPMENT_ID)).status).toBe('Delivered');
    const statuses = h.publisher.messages.filter((m) => m.topic === 'tracking.status-changed').map((m) => m.payload.status);
    expect(statuses).toEqual(['PickedUp', 'InTransit', 'OutForDelivery', 'Delivered']);
    expect(h.publisher.messages.at(-1).payload.proofOfDelivery).toMatchObject({ recipientName: 'Jonas Weber', deliveredAt: NOW.toISOString() });
    expect((await h.repo.events(SHIPMENT_ID)).at(-1)).toMatchObject({ type: 'Delivered', sequence: 6, source: 'courier' });
  });

  it('rejects skipping steps, other couriers and deliveries without proof', async () => {
    await assign();
    await expect(h.service.updateStatus(SHIPMENT_ID, courier, { status: 'Delivered', proof: { recipientName: 'a', signatureName: 'b' } })).rejects.toMatchObject({ statusCode: 409 });
    await expect(h.service.updateStatus(SHIPMENT_ID, { role: 'Courier', courierId: 'someone-else' }, { status: 'PickedUp' })).rejects.toMatchObject({ statusCode: 403 });
    await expect(h.service.updateStatus(SHIPMENT_ID, courier, { status: 'Delivered' })).rejects.toMatchObject({ statusCode: 400 });
    await expect(h.service.updateStatus(SHIPMENT_ID, courier, { status: 'Failed' })).rejects.toMatchObject({ statusCode: 400 });
  });

  it('rolls the append back when Kafka does not acknowledge the event', async () => {
    await assign();
    h.publisher.fail = true;
    await expect(h.service.updateStatus(SHIPMENT_ID, courier, { status: 'PickedUp' })).rejects.toThrow('kafka down');
    expect((await h.repo.getView(SHIPMENT_ID)).status).toBe('Assigned');
    expect((await h.repo.events(SHIPMENT_ID)).length).toBe(2);
  });

  it('shows the cached position and fetches a fresh ETA once the parcel is on its way', async () => {
    await assign();
    await h.service.updateStatus(SHIPMENT_ID, courier, { status: 'PickedUp' });
    await h.service.recordPing(COURIER_ID, { lat: 52.51, lng: 13.4, recordedAt: NOW.toISOString() });

    const view = await h.service.publicView('PT-2024-ABC234');

    expect(view.status).toBe('PickedUp');
    expect(view.lastLocation).toMatchObject({ lat: 52.51, lng: 13.4 });
    expect(view.eta).toMatchObject({ minutesRemaining: 17 });
    expect(h.eta.calls).toHaveLength(1);
    expect(view.to).toEqual({ city: 'Berlin', postalCode: '10719', lat: 52.5, lng: 13.33 });
    expect(view.courierName).toBe('Lena');
    expect(view.timeline.map((t) => t.status)).toEqual(['Created', 'Assigned', 'PickedUp']);

    await h.service.publicView('PT-2024-ABC234');
    expect(h.eta.calls).toHaveLength(1); // cached now
  });

  it('keeps the newest ETA when recalculations arrive out of order', async () => {
    await assign();
    await h.handlers['eta.recalculated']({ eventId: 'a', payload: { shipmentId: SHIPMENT_ID, etaAt: '2024-04-22T12:30:00Z', minutesRemaining: 30, distanceKm: 6, calculatedAt: '2024-04-22T12:00:00Z' } });
    await h.handlers['eta.recalculated']({ eventId: 'b', payload: { shipmentId: SHIPMENT_ID, etaAt: '2024-04-22T12:40:00Z', minutesRemaining: 40, distanceKm: 8, calculatedAt: '2024-04-22T11:58:00Z' } });

    expect((await h.etaCache.get(SHIPMENT_ID)).minutesRemaining).toBe(30);
    expect(await h.repo.isProcessed('b', 'tracking')).toBe(true);
  });

  it('closes the timeline once when the shipment is cancelled (compensation)', async () => {
    await assign();
    const cancelled = { eventId: 'c1', payload: { shipmentId: SHIPMENT_ID, trackingNumber: 'PT-2024-ABC234', courierId: COURIER_ID, reason: 'Changed my mind', cancelledAt: NOW.toISOString() } };
    await h.handlers['shipments.cancelled'](cancelled);
    await h.handlers['shipments.cancelled']({ ...cancelled, eventId: 'c2' });

    expect((await h.repo.events(SHIPMENT_ID)).map((e) => e.type)).toEqual(['Created', 'Assigned', 'Cancelled']);
    expect((await h.service.boardView()).couriers[0].activeShipments).toBe(0);
  });

  it('rebuilds projections and the board from the log', async () => {
    await assign();
    await h.service.updateStatus(SHIPMENT_ID, courier, { status: 'PickedUp' });
    h.repo.views.clear();
    h.redis.data.clear();

    await h.service.rebuildProjections();

    expect(await h.repo.getView(SHIPMENT_ID)).toMatchObject({ status: 'PickedUp', courierId: COURIER_ID });
    const board = await h.service.boardView();
    expect(board.byStatus).toEqual({ PickedUp: 1 });
    expect(board.today).toMatchObject({ created: 1, assigned: 1, pickedUp: 1 });
  });
});
