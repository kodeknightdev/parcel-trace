'use strict';

const { randomUUID } = require('node:crypto');

const Topics = Object.freeze({
  ShipmentCreated: 'shipments.created',
  ShipmentCancelled: 'shipments.cancelled',
  CourierAssigned: 'dispatch.courier-assigned',
  LocationUpdated: 'tracking.location-updated',
  StatusChanged: 'tracking.status-changed',
  EtaRecalculated: 'eta.recalculated',
  retry: (group) => `${group}.retry`,
  deadLetter: (group) => `${group}.dlq`,
});

const EventTypes = Object.freeze({
  LocationUpdated: 'LocationUpdated',
  StatusChanged: 'StatusChanged',
});

const Headers = Object.freeze({
  EventType: 'event-type',
  TraceParent: 'traceparent',
  RetryCount: 'retry-count',
  OriginalTopic: 'original-topic',
  Error: 'error',
});

function envelope(eventType, payload, now = new Date()) {
  return {
    eventId: randomUUID(),
    eventType,
    schemaVersion: 1,
    occurredAt: now.toISOString(),
    payload,
  };
}

module.exports = { Topics, EventTypes, Headers, envelope };
