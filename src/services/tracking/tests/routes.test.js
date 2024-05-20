import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { buildApp, readinessChecks } from '../src/app';
import { createHarness } from './setup';
import { SHIPMENT_ID, COURIER_ID, createdEvent, assignedEvent, testConfig } from './fakes';

describe('HTTP routes', () => {
  let app;
  let h;

  beforeEach(async () => {
    h = createHarness();
    app = await buildApp({ config: testConfig, service: h.service, repo: h.repo, locations: h.locations, readiness: readinessChecks({ postgres: () => h.repo.ping() }), logger: false });
    await h.handlers['shipments.created']({ eventId: '1', payload: createdEvent() });
    await h.handlers['dispatch.courier-assigned']({ eventId: '2', payload: assignedEvent() });
  });

  afterEach(() => app.close());

  const token = (claims) =>
    app.jwt.sign({ sub: 'u1', email: 'x@parceltrace.local', name: 'Test', ...claims }, { iss: 'parcel-trace-identity', aud: 'parcel-trace', expiresIn: '5m' });

  it('serves the public tracking view without a token', async () => {
    const res = await app.inject({ method: 'GET', url: '/api/tracking/pt-2024-abc234' });
    expect(res.statusCode).toBe(200);
    expect(res.json()).toMatchObject({ trackingNumber: 'PT-2024-ABC234', status: 'Assigned', lastLocation: null });
  });

  it('returns problem details for unknown and malformed tracking numbers', async () => {
    expect((await app.inject({ method: 'GET', url: '/api/tracking/PT-2024-ZZZZZZ' })).statusCode).toBe(404);
    const bad = await app.inject({ method: 'GET', url: '/api/tracking/not-a-number' });
    expect(bad.statusCode).toBe(400);
    expect(bad.headers['content-type']).toContain('application/problem+json');
  });

  it('accepts a courier ping and uses the courier id from the token', async () => {
    const res = await app.inject({
      method: 'POST',
      url: '/api/tracking/pings',
      headers: { authorization: `Bearer ${token({ role: 'Courier', courier_id: COURIER_ID })}` },
      payload: { lat: 52.5, lng: 13.4, speedKmh: 18 },
    });
    expect(res.statusCode).toBe(202);
    expect(h.publisher.messages[0].payload.courierId).toBe(COURIER_ID);
  });

  it('requires a token and the right role', async () => {
    expect((await app.inject({ method: 'POST', url: '/api/tracking/pings', payload: { lat: 1, lng: 1 } })).statusCode).toBe(401);
    const customer = await app.inject({ method: 'GET', url: '/api/tracking/board', headers: { authorization: `Bearer ${token({ role: 'Customer' })}` } });
    expect(customer.statusCode).toBe(403);
  });

  it('serves the dispatcher board read model', async () => {
    const res = await app.inject({ method: 'GET', url: '/api/tracking/board', headers: { authorization: `Bearer ${token({ role: 'Dispatcher' })}` } });
    expect(res.statusCode).toBe(200);
    expect(res.json().byStatus).toEqual({ Assigned: 1 });
  });

  it('validates status updates', async () => {
    const res = await app.inject({
      method: 'POST',
      url: `/api/tracking/shipments/${SHIPMENT_ID}/status`,
      headers: { authorization: `Bearer ${token({ role: 'Courier', courier_id: COURIER_ID })}` },
      payload: { status: 'Teleported' },
    });
    expect(res.statusCode).toBe(400);
  });

  it('reports readiness', async () => {
    const res = await app.inject({ method: 'GET', url: '/health/ready' });
    expect(res.json()).toEqual({ status: 'Healthy', checks: [{ name: 'postgres', status: 'Healthy' }] });
  });
});
