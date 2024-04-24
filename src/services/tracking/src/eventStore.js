'use strict';

const { apply } = require('./projection');

// locks the view row first so appends for one shipment are serialized
async function appendEvent(tx, { shipmentId, type, data, source, sourceEventId, occurredAt }) {
  const previous = await tx.lockView(shipmentId);
  const event = await tx.insertEvent({
    shipmentId,
    sequence: (previous?.lastSequence ?? 0) + 1,
    type,
    data,
    source,
    sourceEventId,
    occurredAt: new Date(occurredAt),
  });
  if (!event) return null;

  const next = apply(previous, event);
  await tx.saveView(next);
  return { event, previous, next };
}

module.exports = { appendEvent };
