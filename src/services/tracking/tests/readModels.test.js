import { beforeEach, describe, expect, it } from 'vitest';
import { createHarness } from './setup';
import { COURIER_ID, createdEvent, assignedEvent } from './fakes';

describe('read models (CQRS) and cache-aside', () => {
  let h;

  beforeEach(() => {
    h = createHarness();
  });

  it('keeps the dispatcher board in step with the event log', async () => {
    await h.handlers['shipments.created']({ eventId: '1', payload: createdEvent() });
    await h.handlers['shipments.created']({ eventId: '2', payload: createdEvent({ shipmentId: 's-2', trackingNumber: 'PT-2024-ZZZ234' }) });
    await h.handlers['dispatch.courier-assigned']({ eventId: '3', payload: assignedEvent() });

    const board = await h.service.boardView();

    expect(board.byStatus).toEqual({ Created: 1, Assigned: 1 });
    expect(board.today).toMatchObject({ created: 2, assigned: 1, delivered: 0 });
    expect(board.couriers).toEqual([{ courierId: COURIER_ID, activeShipments: 1, location: null }]);
  });

  it('serves the latest position from Redis and falls back to Postgres on a miss', async () => {
    await h.locations.record({ courierId: COURIER_ID, lat: 52.5, lng: 13.4, recordedAt: new Date('2024-04-22T11:00:00Z') });
    h.redis.data.clear();

    const fromDb = await h.locations.get(COURIER_ID);
    expect(fromDb).toMatchObject({ lat: 52.5, lng: 13.4 });
    expect(await h.locations.readCache(COURIER_ID)).toMatchObject({ lat: 52.5 });
  });

  it('never lets an older ping overwrite the cached position', async () => {
    await h.locations.record({ courierId: COURIER_ID, lat: 1, lng: 1, recordedAt: new Date('2024-04-22T11:00:00Z') });
    await h.locations.record({ courierId: COURIER_ID, lat: 2, lng: 2, recordedAt: new Date('2024-04-22T10:00:00Z') });

    expect((await h.locations.get(COURIER_ID)).lat).toBe(1);
    expect(h.repo.pings).toHaveLength(2);
  });

  it('loads an ETA into the cache on first read only', async () => {
    await h.repo.saveEta('s1', { etaAt: '2024-04-22T12:30:00Z', minutesRemaining: 30, calculatedAt: '2024-04-22T12:00:00Z' });

    await h.etaCache.get('s1');
    const second = await h.etaCache.get('s1');

    expect(second.minutesRemaining).toBe(30);
    expect(h.redis.data.has('eta:s1')).toBe(true);
  });
});
