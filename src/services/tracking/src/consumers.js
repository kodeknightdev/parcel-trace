'use strict';

const { Topics } = require('./events');

const CONSUMER = 'tracking';

function buildHandlers(service, repo) {
  const once = (fn) => async (event) => {
    if (await repo.isProcessed(event.eventId, CONSUMER)) return;
    await fn(event.eventId, event.payload);
    await repo.markProcessed(event.eventId, CONSUMER);
  };

  return {
    [Topics.ShipmentCreated]: once((id, p) => service.onShipmentCreated(id, p)),
    [Topics.ShipmentCancelled]: once((id, p) => service.onShipmentCancelled(id, p)),
    [Topics.CourierAssigned]: once((id, p) => service.onCourierAssigned(id, p)),
    [Topics.EtaRecalculated]: once((id, p) => service.onEtaRecalculated(id, p)),
  };
}

module.exports = { buildHandlers, CONSUMER };
