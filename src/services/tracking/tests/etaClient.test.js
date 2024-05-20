import { describe, expect, it } from 'vitest';
import { EtaClient } from '../src/etaClient';

const request = { origin: { lat: 1, lng: 1 }, destination: { lat: 2, lng: 2 }, serviceLevel: 'Standard' };

describe('EtaClient (opossum circuit breaker)', () => {
  it('retries a failed attempt and returns the estimate', async () => {
    let calls = 0;
    const fetchImpl = async () => {
      calls += 1;
      if (calls === 1) throw new Error('connection reset');
      return { ok: true, status: 200, json: async () => ({ minutes: 12 }) };
    };
    const client = new EtaClient({ baseUrl: 'http://eta', retries: 2, fetchImpl });

    expect(await client.estimate(request)).toEqual({ minutes: 12 });
    expect(calls).toBe(2);
    client.shutdown();
  });

  it('falls back to null and opens the circuit when eta keeps failing', async () => {
    let calls = 0;
    const fetchImpl = async () => {
      calls += 1;
      return { ok: false, status: 503 };
    };
    const client = new EtaClient({ baseUrl: 'http://eta', retries: 0, fetchImpl, breaker: { volumeThreshold: 3, resetTimeout: 60_000 } });

    for (let i = 0; i < 3; i++) expect(await client.estimate(request)).toBeNull();
    expect(client.isOpen).toBe(true);

    const before = calls;
    expect(await client.estimate(request)).toBeNull();
    expect(calls).toBe(before); // circuit open, no call
    client.shutdown();
  });

  it('does not retry or trip on a 4xx', async () => {
    let calls = 0;
    const client = new EtaClient({ baseUrl: 'http://eta', retries: 2, fetchImpl: async () => { calls += 1; return { ok: false, status: 400 }; } });

    expect(await client.estimate(request)).toBeNull();
    expect(calls).toBe(1);
    expect(client.isOpen).toBe(false);
    client.shutdown();
  });
});
