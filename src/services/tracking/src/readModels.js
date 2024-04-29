'use strict';

const { onTheWay } = require('./stateMachine');

const STATUS = 'board:status';
const DAY = (day) => `board:day:${day}`;
const COURIER = (courierId) => `board:courier:${courierId}`;
const COURIERS = 'board:couriers';
const DAY_TTL_SECONDS = 3 * 24 * 60 * 60;

const dayOf = (date) => new Date(date).toISOString().slice(0, 10);
const COUNTED = { Created: 'created', Assigned: 'assigned', PickedUp: 'pickedUp', Delivered: 'delivered', Failed: 'failed', Cancelled: 'cancelled' };

// dashboard read model, rebuildable from the event log
class DispatchBoard {
  constructor(redis) {
    this.redis = redis;
  }

  async apply({ previous, next, event }) {
    const m = this.redis.multi();
    if (previous?.status !== next.status) {
      if (previous) m.hincrby(STATUS, previous.status, -1);
      m.hincrby(STATUS, next.status, 1);
    }
    if (COUNTED[event.type]) {
      m.hincrby(DAY(dayOf(event.occurredAt)), COUNTED[event.type], 1).expire(DAY(dayOf(event.occurredAt)), DAY_TTL_SECONDS);
    }
    if (previous?.courierId && previous.courierId !== next.courierId) m.srem(COURIER(previous.courierId), next.shipmentId);
    if (next.courierId) {
      m.sadd(COURIERS, next.courierId);
      if (next.status === 'Assigned' || onTheWay.includes(next.status)) m.sadd(COURIER(next.courierId), next.shipmentId);
      else m.srem(COURIER(next.courierId), next.shipmentId);
    }
    await m.exec();
  }

  async read(day, locations) {
    const [byStatus, today, courierIds] = await Promise.all([this.redis.hgetall(STATUS), this.redis.hgetall(DAY(day)), this.redis.smembers(COURIERS)]);
    const couriers = await Promise.all(
      courierIds.map(async (courierId) => ({
        courierId,
        activeShipments: (await this.redis.smembers(COURIER(courierId))).length,
        location: await locations.get(courierId),
      })),
    );
    return {
      day,
      byStatus: numbers(byStatus),
      today: { created: 0, assigned: 0, pickedUp: 0, delivered: 0, failed: 0, cancelled: 0, ...numbers(today) },
      couriers,
    };
  }

  async rebuild(views, recentEvents) {
    const courierIds = await this.redis.smembers(COURIERS);
    const days = [...new Set(recentEvents.map((e) => dayOf(e.occurredAt)))];
    await this.redis.del(STATUS, COURIERS, ...courierIds.map(COURIER), ...days.map(DAY));

    const m = this.redis.multi();
    for (const v of views) {
      m.hincrby(STATUS, v.status, 1);
      if (v.courierId) {
        m.sadd(COURIERS, v.courierId);
        if (v.status === 'Assigned' || onTheWay.includes(v.status)) m.sadd(COURIER(v.courierId), v.shipmentId);
      }
    }
    for (const e of recentEvents) {
      if (COUNTED[e.type]) m.hincrby(DAY(dayOf(e.occurredAt)), COUNTED[e.type], 1);
    }
    for (const day of days) m.expire(DAY(day), DAY_TTL_SECONDS);
    await m.exec();
  }
}

const numbers = (hash) => Object.fromEntries(Object.entries(hash ?? {}).map(([k, v]) => [k, Number(v)]).filter(([, v]) => v !== 0));

module.exports = { DispatchBoard, dayOf };
