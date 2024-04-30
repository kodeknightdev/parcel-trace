'use strict';

const { HttpError } = require('../trackingService');

const point = {
  type: 'object',
  required: ['lat', 'lng'],
  properties: { lat: { type: 'number', minimum: -90, maximum: 90 }, lng: { type: 'number', minimum: -180, maximum: 180 } },
};

const uuid = { type: 'string', format: 'uuid' };
const STAFF = ['Dispatcher', 'Admin'];

module.exports = async function trackingRoutes(fastify, { service, repo, locations }) {
  fastify.get('/api/tracking/board', {
    preHandler: fastify.authorize(...STAFF),
    schema: { tags: ['Dispatch board'], summary: 'Read model for the dispatcher dashboard: shipments per status, today\'s counts, couriers on the road' },
  }, async () => service.boardView());

  fastify.post('/api/tracking/admin/rebuild-projections', {
    preHandler: fastify.authorize('Admin'),
    schema: { tags: ['Dispatch board'], summary: 'Rebuild shipment projections and the dispatcher board from the event log' },
  }, async () => service.rebuildProjections());

  fastify.post('/api/tracking/pings', {
    preHandler: fastify.authorize('Courier', ...STAFF),
    schema: {
      tags: ['Tracking'],
      summary: 'Courier location ping (couriers post for themselves; staff may pass courierId)',
      body: {
        type: 'object',
        required: ['lat', 'lng'],
        properties: {
          ...point.properties,
          speedKmh: { type: 'number', minimum: 0, maximum: 200 },
          recordedAt: { type: 'string', format: 'date-time' },
          courierId: uuid,
        },
      },
    },
  }, async (request, reply) => {
    const { user, body } = request;
    const courierId = user.role === 'Courier' ? user.courierId : body.courierId;
    if (!courierId) throw new HttpError(400, 'courierId required', 'Staff pings must name the courier.');
    const result = await service.recordPing(courierId, body);
    return reply.code(202).send(result);
  });

  fastify.post('/api/tracking/shipments/:shipmentId/status', {
    preHandler: fastify.authorize('Courier', ...STAFF),
    schema: {
      tags: ['Tracking'],
      summary: 'Report pick-up, transit, out-for-delivery, delivery (with proof) or failure',
      params: { type: 'object', properties: { shipmentId: uuid } },
      body: {
        type: 'object',
        required: ['status'],
        properties: {
          status: { type: 'string', enum: ['PickedUp', 'InTransit', 'OutForDelivery', 'Delivered', 'Failed'] },
          note: { type: 'string', maxLength: 300 },
          lat: point.properties.lat,
          lng: point.properties.lng,
          proof: {
            type: 'object',
            required: ['recipientName', 'signatureName'],
            properties: {
              recipientName: { type: 'string', minLength: 1, maxLength: 120 },
              signatureName: { type: 'string', minLength: 1, maxLength: 120 },
              photoUrl: { type: 'string', format: 'uri', maxLength: 500 },
            },
          },
        },
      },
    },
  }, async (request) => {
    const { shipment, changed } = await service.updateStatus(request.params.shipmentId, request.user, request.body);
    return { shipmentId: shipment.shipmentId, trackingNumber: shipment.trackingNumber, status: shipment.status, changed };
  });

  fastify.get('/api/tracking/shipments/:shipmentId/timeline', {
    preHandler: fastify.authorize(),
    schema: { tags: ['Tracking'], summary: 'Full event log incl. proof of delivery (owner, courier, staff)', params: { type: 'object', properties: { shipmentId: uuid } } },
  }, async (request) => {
    const shipment = await repo.getView(request.params.shipmentId);
    const { user } = request;
    const allowed = shipment && (STAFF.includes(user.role) || shipment.customerId === user.id || (user.courierId && shipment.courierId === user.courierId));
    if (!allowed) throw new HttpError(404, 'Shipment not found');
    return { shipmentId: shipment.shipmentId, trackingNumber: shipment.trackingNumber, status: shipment.status, events: await service.timeline(shipment.shipmentId) };
  });

  fastify.get('/api/tracking/couriers/locations', {
    preHandler: fastify.authorize(...STAFF),
    schema: { tags: ['Couriers'], summary: 'Latest known position of every courier' },
  }, async () => locations.all());

  fastify.get('/api/tracking/couriers/:courierId/history', {
    preHandler: fastify.authorize(...STAFF),
    schema: {
      tags: ['Couriers'],
      summary: 'Recent pings of one courier',
      params: { type: 'object', properties: { courierId: uuid } },
      querystring: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 1000, default: 100 } } },
    },
  }, async (request) => repo.pingHistory(request.params.courierId, request.query.limit));

  fastify.get('/api/tracking/:trackingNumber', {
    config: { public: true },
    schema: {
      tags: ['Public'],
      summary: 'Public tracking: status timeline, ETA and last known location',
      params: { type: 'object', properties: { trackingNumber: { type: 'string', pattern: '^[Pp][Tt]-\\d{4}-[A-Za-z0-9]{6}$' } } },
    },
  }, async (request) => {
    const view = await service.publicView(request.params.trackingNumber.toUpperCase());
    if (!view) throw new HttpError(404, 'Not found', `No shipment with tracking number ${request.params.trackingNumber.toUpperCase()}.`);
    return view;
  });
};
