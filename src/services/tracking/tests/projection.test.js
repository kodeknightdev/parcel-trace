import { describe, expect, it } from 'vitest';
import { apply, project } from '../src/projection';

const id = 's1';
const ev = (sequence, type, data = {}) => ({ shipmentId: id, sequence, type, data, occurredAt: new Date(2024, 3, 22, 10, sequence) });
const created = ev(1, 'Created', {
  trackingNumber: 'PT-2024-ABC234', customerId: 'c', serviceLevel: 'Standard', weightKg: 2, slaDueAt: '2024-04-24T10:00:00Z',
  pickup: { city: 'Berlin', lat: 1, lng: 2 }, dropoff: { city: 'Berlin', postalCode: '10719', lat: 3, lng: 4 },
});

describe('projection (event sourcing light)', () => {
  it('folds the log into the current state', () => {
    const state = project([
      created,
      ev(2, 'Assigned', { courierId: 'k1', courierName: 'Lena Fischer' }),
      ev(3, 'PickedUp'),
      ev(4, 'OutForDelivery'),
      ev(5, 'Delivered', { proof: { recipientName: 'Jonas', deliveredAt: '2024-04-22T11:00:00Z' } }),
    ]);

    expect(state).toMatchObject({ status: 'Delivered', courierName: 'Lena Fischer', deliveredTo: 'Jonas', lastSequence: 5, trackingNumber: 'PT-2024-ABC234' });
  });

  it('gives the same result incrementally and as a full rebuild', () => {
    const events = [created, ev(2, 'Assigned', { courierId: 'k1', courierName: 'A' }), ev(3, 'Assigned', { courierId: 'k2', courierName: 'B' }), ev(4, 'Cancelled')];
    let incremental = null;
    for (const e of events) incremental = apply(incremental, e);

    expect(project(events)).toEqual(incremental);
    expect(incremental).toMatchObject({ status: 'Cancelled', courierId: 'k2' });
  });

  it('refuses events for a shipment that was never created', () => {
    expect(() => project([ev(1, 'PickedUp')])).toThrow(/before Created/);
  });

  it('is pure: applying does not change the previous state', () => {
    const before = apply(null, created);
    const frozen = structuredClone(before);
    apply(before, ev(2, 'Assigned', { courierId: 'k1', courierName: 'A' }));
    expect(before).toEqual(frozen);
  });
});
