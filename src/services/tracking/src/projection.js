'use strict';

const EventTypes = Object.freeze({
  Created: 'Created',
  Assigned: 'Assigned',
  PickedUp: 'PickedUp',
  InTransit: 'InTransit',
  OutForDelivery: 'OutForDelivery',
  Delivered: 'Delivered',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
});

// current state = fold over the shipment's events
function apply(state, event) {
  const d = event.data ?? {};
  const base = { lastSequence: event.sequence, updatedAt: event.occurredAt };

  if (event.type === EventTypes.Created) {
    return {
      shipmentId: event.shipmentId,
      trackingNumber: d.trackingNumber,
      customerId: d.customerId,
      serviceLevel: d.serviceLevel,
      status: 'Created',
      courierId: null,
      courierName: null,
      pickup: d.pickup,
      dropoff: d.dropoff,
      weightKg: Number(d.weightKg),
      slaDueAt: d.slaDueAt,
      createdAt: event.occurredAt,
      deliveredTo: null,
      deliveredAt: null,
      ...base,
    };
  }

  if (!state) throw new Error(`event ${event.type} #${event.sequence} before Created for ${event.shipmentId}`);

  switch (event.type) {
    case EventTypes.Assigned:
      return { ...state, ...base, status: 'Assigned', courierId: d.courierId, courierName: d.courierName };
    case EventTypes.Delivered:
      return { ...state, ...base, status: 'Delivered', deliveredTo: d.proof?.recipientName ?? null, deliveredAt: d.proof?.deliveredAt ?? event.occurredAt };
    default:
      return { ...state, ...base, status: event.type };
  }
}

const project = (events) => events.reduce(apply, null);

module.exports = { EventTypes, apply, project };
