'use strict';

const KEY = (shipmentId) => `eta:${shipmentId}`;
const TTL_SECONDS = 15 * 60;

class EtaCache {
  constructor(redis, repo) {
    this.redis = redis;
    this.repo = repo;
  }

  async get(shipmentId) {
    const cached = await this.redis.get(KEY(shipmentId));
    if (cached) return JSON.parse(cached);

    const stored = await this.repo.getEta(shipmentId);
    if (!stored) return null;
    const eta = normalize(stored);
    await this.redis.set(KEY(shipmentId), JSON.stringify(eta), 'EX', TTL_SECONDS);
    return eta;
  }

  async put(shipmentId, eta) {
    const stored = await this.repo.saveEta(shipmentId, eta);
    if (stored) await this.redis.set(KEY(shipmentId), JSON.stringify(normalize(eta)), 'EX', TTL_SECONDS);
    return stored;
  }

  async evict(shipmentId) {
    await this.redis.del(KEY(shipmentId));
  }
}

const iso = (v) => (v === null || v === undefined ? null : new Date(v).toISOString());

function normalize(eta) {
  return {
    etaAt: iso(eta.etaAt),
    minutesRemaining: eta.minutesRemaining ?? null,
    distanceKm: eta.distanceKm ?? null,
    calculatedAt: iso(eta.calculatedAt),
  };
}

module.exports = { EtaCache };
