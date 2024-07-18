#!/usr/bin/env node
'use strict';

const { parseArgs } = require('node:util');

const { values: opts } = parseArgs({
  options: {
    gateway: { type: 'string', default: process.env.GATEWAY_URL ?? 'http://localhost:8080' },
    password: { type: 'string', default: process.env.SEED_DEMO_PASSWORD ?? 'Demo@12345' },
    couriers: { type: 'string', default: 'courier1@parceltrace.local,courier2@parceltrace.local,courier3@parceltrace.local' },
    interval: { type: 'string', default: '3' }, // seconds between ticks
    speed: { type: 'string', default: '40' }, // km/h; raise it to fast-forward
    'reject-rate': { type: 'string', default: '0' }, // 0..1
    once: { type: 'boolean', default: false },
    idle: { type: 'string', default: '20' },
    help: { type: 'boolean', short: 'h', default: false },
  },
});

if (opts.help) {
  console.log('node simulator.js [--gateway URL] [--couriers a@x,b@y] [--password P] [--interval s] [--speed kmh] [--reject-rate 0..1] [--once] [--idle ticks]');
  process.exit(0);
}

const INTERVAL_S = Number(opts.interval);
const REJECT_RATE = Number(opts['reject-rate']);
const SPEED_KMH = Number(opts.speed);
const ARRIVED_KM = 0.05;
const OUT_FOR_DELIVERY_KM = 0.8;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const log = (who, msg) => console.log(`${new Date().toISOString().slice(11, 19)} ${who.padEnd(6)} ${msg}`);

function haversineKm(a, b) {
  const rad = (d) => (d * Math.PI) / 180;
  const dLat = rad(b.lat - a.lat);
  const dLng = rad(b.lng - a.lng);
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(dLng / 2) ** 2;
  return 6371 * 2 * Math.atan2(Math.sqrt(h), Math.sqrt(1 - h));
}

function moveTowards(from, to, stepKm) {
  const distance = haversineKm(from, to);
  if (distance <= stepKm) return { ...to };
  const f = stepKm / distance;
  return { lat: from.lat + (to.lat - from.lat) * f, lng: from.lng + (to.lng - from.lng) * f };
}

async function api(token, method, path, body, attempt = 1) {
  const res = await fetch(`${opts.gateway}${path}`, {
    method,
    headers: { 'content-type': 'application/json', ...(token ? { authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (res.status === 429 && attempt <= 3) {
    // rate limited, wait and retry
    const waitS = Number(res.headers.get('retry-after') ?? 10);
    await sleep(Math.max(1, waitS) * 1000);
    return api(token, method, path, body, attempt + 1);
  }
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  if (!res.ok) {
    const err = new Error(`${method} ${path} -> ${res.status} ${data?.detail ?? data?.title ?? ''}`.trim());
    err.status = res.status;
    throw err;
  }
  return data;
}

class SimulatedCourier {
  constructor(email) {
    this.email = email;
    this.name = email.split('@')[0];
    this.idleTicks = 0;
    this.delivered = 0;
  }

  async start() {
    const auth = await api(null, 'POST', '/api/auth/login', { email: this.email, password: opts.password });
    this.token = auth.accessToken;
    const me = await api(this.token, 'GET', '/api/couriers/me');
    this.position = me.lastLocation ?? me.homeBase;
    if (!me.isAvailable) {
      await api(this.token, 'PUT', '/api/couriers/me/availability', { isAvailable: true });
      log(this.name, 'went on duty');
    }
    log(this.name, `${me.name} (${me.vehicleType}) starting at ${this.position.lat.toFixed(4)}, ${this.position.lng.toFixed(4)}`);
  }

  async answerOffers() {
    const offers = await api(this.token, 'GET', '/api/dispatch/offers/me');
    for (const offer of offers) {
      if (Math.random() < REJECT_RATE) {
        await api(this.token, 'POST', `/api/dispatch/offers/${offer.shipmentId}/reject`, { reason: 'Simulated: too busy' });
        log(this.name, `${offer.trackingNumber} offer rejected`);
        continue;
      }
      try {
        await api(this.token, 'POST', `/api/dispatch/offers/${offer.shipmentId}/accept`);
        log(this.name, `${offer.trackingNumber} offer accepted`);
      } catch (err) {
        log(this.name, `${offer.trackingNumber} accept failed: ${err.message}`);
      }
    }
    return offers.length;
  }

  async tick() {
    if ((await this.answerOffers()) > 0) this.idleTicks = 0;
    const route = await api(this.token, 'GET', '/api/dispatch/routes/me');
    const stop = route.stops.find((s) => s.status === 'Active');
    const stepKm = (SPEED_KMH * INTERVAL_S) / 3600;

    if (!stop) {
      this.idleTicks += 1;
      if (this.idleTicks % 5 === 1) await this.ping(0);
      return;
    }
    this.idleTicks = 0;

    const status = stop.shipmentStatus;
    const target = status === 'Assigned' ? stop.pickup : stop.dropoff;
    this.position = moveTowards(this.position, target, stepKm);
    await this.ping(SPEED_KMH);
    const left = haversineKm(this.position, target);

    if (status === 'Assigned' && left <= ARRIVED_KM) {
      await this.report(stop, 'PickedUp');
    } else if (status === 'PickedUp') {
      await this.report(stop, left <= OUT_FOR_DELIVERY_KM ? 'OutForDelivery' : 'InTransit');
    } else if (status === 'InTransit' && left <= OUT_FOR_DELIVERY_KM) {
      await this.report(stop, 'OutForDelivery');
    } else if (status === 'OutForDelivery' && left <= ARRIVED_KM) {
      await this.report(stop, 'Delivered', {
        proof: { recipientName: stop.recipientName, signatureName: stop.recipientName.split(' ').map((p, i) => (i === 0 ? `${p[0]}.` : p)).join(' ') },
      });
      this.delivered += 1;
    }
  }

  async ping(speedKmh) {
    await api(this.token, 'POST', '/api/tracking/pings', { lat: this.position.lat, lng: this.position.lng, speedKmh });
  }

  async report(stop, status, extra = {}) {
    try {
      await api(this.token, 'POST', `/api/tracking/shipments/${stop.shipmentId}/status`, { status, lat: this.position.lat, lng: this.position.lng, ...extra });
      log(this.name, `${stop.trackingNumber} -> ${status}`);
      // give dispatch a moment to catch up
      await sleep(500);
    } catch (err) {
      log(this.name, `${stop.trackingNumber} ${status} failed: ${err.message}`);
    }
  }
}

async function main() {
  const couriers = opts.couriers.split(',').map((e) => new SimulatedCourier(e.trim()));
  console.log(`courier simulator -> ${opts.gateway} · ${couriers.length} couriers · every ${INTERVAL_S}s at ${SPEED_KMH} km/h${opts.once ? ' · --once' : ''}`);
  for (const c of couriers) await c.start();

  let stopping = false;
  process.on('SIGINT', () => {
    stopping = true;
  });

  while (!stopping) {
    await Promise.all(
      couriers.map((c) =>
        c.tick().catch((err) => {
          log(c.name, `tick failed: ${err.message}`);
          if (err.status === 401) return c.start(); // token expired
        }),
      ),
    );

    if (opts.once && couriers.every((c) => c.idleTicks >= Number(opts.idle))) break;
    await sleep(INTERVAL_S * 1000);
  }

  const total = couriers.reduce((n, c) => n + c.delivered, 0);
  console.log(`done: ${total} parcel(s) delivered`);
}

main().catch((err) => {
  console.error(err.message);
  process.exit(1);
});
