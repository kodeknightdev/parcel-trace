'use strict';

const KEY = (courierId) => `courier:${courierId}:location`;
const KNOWN = 'couriers:known';
const TTL_SECONDS = 24 * 60 * 60;

class LocationStore {
  constructor(redis, repo) {
    this.redis = redis;
    this.repo = repo;
  }

  async record(ping) {
    await this.repo.addPing(ping);
    const cached = await this.readCache(ping.courierId);
    if (cached && new Date(cached.recordedAt) >= new Date(ping.recordedAt)) return false;
    await this.writeCache(ping);
    return true;
  }

  async get(courierId) {
    const cached = await this.readCache(courierId);
    if (cached) return cached;

    const latest = await this.repo.latestPing(courierId);
    if (!latest) return null;
    await this.writeCache(latest);
    return { ...latest, recordedAt: new Date(latest.recordedAt).toISOString() };
  }

  async all() {
    const ids = await this.redis.smembers(KNOWN);
    const locations = await Promise.all(ids.map((id) => this.get(id)));
    return locations.filter(Boolean);
  }

  async readCache(courierId) {
    const h = await this.redis.hgetall(KEY(courierId));
    if (!h || !h.lat) return null;
    return {
      courierId,
      lat: Number(h.lat),
      lng: Number(h.lng),
      speedKmh: h.speedKmh === '' ? null : Number(h.speedKmh),
      recordedAt: h.recordedAt,
    };
  }

  async writeCache(location) {
    await this.redis
      .multi()
      .hset(KEY(location.courierId), {
        lat: String(location.lat),
        lng: String(location.lng),
        speedKmh: location.speedKmh === undefined || location.speedKmh === null ? '' : String(location.speedKmh),
        recordedAt: new Date(location.recordedAt).toISOString(),
      })
      .expire(KEY(location.courierId), TTL_SECONDS)
      .sadd(KNOWN, location.courierId)
      .exec();
  }

  async ping() {
    await this.redis.ping();
  }
}

module.exports = { LocationStore };
