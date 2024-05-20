'use strict';

const { active } = require('../src/stateMachine');

const clone = (v) => structuredClone(v);

class InMemoryRepo {
  constructor() {
    this.log = [];
    this.views = new Map();
    this.etas = new Map();
    this.pings = [];
    this.processed = new Set();
  }

  async transaction(fn) {
    const snapshot = { log: [...this.log], views: clone(this.views), etas: clone(this.etas) };
    try {
      return await fn(this);
    } catch (err) {
      Object.assign(this, snapshot);
      throw err;
    }
  }

  async insertEvent(e) {
    if (e.sourceEventId && this.log.some((x) => x.sourceEventId === e.sourceEventId)) return null;
    if (this.log.some((x) => x.shipmentId === e.shipmentId && x.sequence === e.sequence)) throw new Error('duplicate sequence');
    const event = { ...clone(e), data: clone(e.data ?? {}), occurredAt: new Date(e.occurredAt) };
    this.log.push(event);
    return clone(event);
  }

  async events(shipmentId) {
    return this.log.filter((e) => e.shipmentId === shipmentId).sort((a, b) => a.sequence - b.sequence).map(clone);
  }

  async eventsSince(since) {
    return this.log.filter((e) => e.occurredAt >= since).map(clone);
  }

  async shipmentIds() {
    return [...new Set(this.log.map((e) => e.shipmentId))];
  }

  async lockView(id) {
    return this.getView(id);
  }

  async getView(id) {
    const v = this.views.get(id);
    return v ? clone(v) : undefined;
  }

  async getViewByTrackingNumber(tn) {
    const v = [...this.views.values()].find((x) => x.trackingNumber === tn);
    return v ? clone(v) : undefined;
  }

  async saveView(v) {
    this.views.set(v.shipmentId, clone(v));
  }

  async activeForCourier(courierId) {
    return [...this.views.values()].filter((s) => s.courierId === courierId && active.includes(s.status)).map(clone);
  }

  async allViews() {
    return [...this.views.values()].map(clone);
  }

  async getEta(id) {
    return this.etas.get(id) ? clone(this.etas.get(id)) : undefined;
  }

  async saveEta(id, eta) {
    const current = this.etas.get(id);
    if (current && new Date(current.calculatedAt) >= new Date(eta.calculatedAt)) return false;
    this.etas.set(id, clone(eta));
    return true;
  }

  async addPing(p) {
    this.pings.push(clone(p));
  }

  async latestPing(courierId) {
    const mine = this.pings.filter((p) => p.courierId === courierId).sort((a, b) => new Date(b.recordedAt) - new Date(a.recordedAt));
    return mine[0] ? clone(mine[0]) : undefined;
  }

  async pingHistory(courierId, limit = 100) {
    return this.pings.filter((p) => p.courierId === courierId).slice(-limit).reverse();
  }

  async isProcessed(eventId, consumer) {
    return this.processed.has(`${consumer}:${eventId}`);
  }

  async markProcessed(eventId, consumer) {
    this.processed.add(`${consumer}:${eventId}`);
  }

  async ping() {}
}

class FakeRedis {
  constructor() {
    this.data = new Map();
    this.calls = [];
  }

  async get(key) {
    this.calls.push(['get', key]);
    return this.data.get(key) ?? null;
  }

  async set(key, value) {
    this.data.set(key, value);
    return 'OK';
  }

  async del(...keys) {
    keys.forEach((k) => this.data.delete(k));
    return keys.length;
  }

  async expire() {
    return 1;
  }

  async hgetall(key) {
    this.calls.push(['hgetall', key]);
    return { ...(this.data.get(key) ?? {}) };
  }

  async hset(key, obj) {
    this.data.set(key, { ...(this.data.get(key) ?? {}), ...obj });
    return 1;
  }

  async hincrby(key, field, by) {
    const h = this.data.get(key) ?? {};
    h[field] = String(Number(h[field] ?? 0) + by);
    this.data.set(key, h);
    return Number(h[field]);
  }

  async sadd(key, member) {
    const s = this.data.get(key) ?? new Set();
    s.add(member);
    this.data.set(key, s);
    return 1;
  }

  async srem(key, member) {
    this.data.get(key)?.delete(member);
    return 1;
  }

  async smembers(key) {
    return [...(this.data.get(key) ?? [])];
  }

  async ping() {
    return 'PONG';
  }

  multi() {
    const queue = [];
    const proxy = new Proxy({}, {
      get: (_, name) => (name === 'exec' ? async () => { for (const [n, args] of queue) await this[n](...args); return []; } : (...args) => { queue.push([name, args]); return proxy; }),
    });
    return proxy;
  }
}

class FakePublisher {
  constructor() {
    this.messages = [];
    this.fail = false;
  }

  async publish(topic, key, eventType, payload) {
    if (this.fail) throw new Error('kafka down');
    this.messages.push({ topic, key, eventType, payload });
  }
}

class FakeEta {
  constructor(result = null) {
    this.result = result;
    this.calls = [];
  }

  async estimate(request) {
    this.calls.push(request);
    return this.result;
  }
}

const SHIPMENT_ID = '7d3b6f5e-2d5c-4b8e-9a63-111111111111';
const COURIER_ID = 'c0de0000-0000-4000-8000-000000000002';

const createdEvent = (overrides = {}) => ({
  shipmentId: SHIPMENT_ID,
  trackingNumber: 'PT-2024-ABC234',
  customerId: 'a11ce000-0000-4000-8000-000000000001',
  serviceLevel: 'Express',
  pickup: { city: 'Berlin', postalCode: '10178', lat: 52.5219, lng: 13.4132 },
  dropoff: { city: 'Berlin', postalCode: '10719', lat: 52.503, lng: 13.332 },
  weightKg: 1.2,
  slaDueAt: '2024-04-22T18:00:00Z',
  etaAt: null,
  createdAt: '2024-04-22T10:00:00Z',
  ...overrides,
});

const assignedEvent = (overrides = {}) => ({
  shipmentId: SHIPMENT_ID,
  trackingNumber: 'PT-2024-ABC234',
  courierId: COURIER_ID,
  courierName: 'Lena Fischer',
  mode: 'Auto',
  assignedAt: '2024-04-22T10:05:00Z',
  ...overrides,
});

const testConfig = {
  logLevel: 'silent',
  jwt: { secret: 'test-signing-key-that-is-long-enough-0123456789', issuer: 'parcel-trace-identity', audience: 'parcel-trace' },
  eta: { staleAfterMs: 10 * 60 * 1000 },
  kafka: { groupId: 'tracking', maxRetries: 3 },
};

module.exports = { InMemoryRepo, FakeRedis, FakePublisher, FakeEta, SHIPMENT_ID, COURIER_ID, createdEvent, assignedEvent, testConfig };
