'use strict';

const CircuitBreaker = require('opossum');

class EtaClient {
  constructor({ baseUrl, timeoutMs = 2000, retries = 2, fetchImpl = fetch, log, breaker = {} }) {
    this.baseUrl = baseUrl.replace(/\/$/, '');
    this.timeoutMs = timeoutMs;
    this.retries = retries;
    this.fetch = fetchImpl;
    this.log = log;

    this.breaker = new CircuitBreaker((request) => this.callWithRetry(request), {
      // covers all attempts
      timeout: timeoutMs * (retries + 1) + 1000,
      errorThresholdPercentage: 50,
      volumeThreshold: 5,
      resetTimeout: 30_000,
      ...breaker,
    });
    this.breaker.fallback(() => null);
    this.breaker.on('open', () => this.log?.warn('eta circuit opened'));
    this.breaker.on('halfOpen', () => this.log?.info('eta circuit half-open, trying one call'));
    this.breaker.on('close', () => this.log?.info('eta circuit closed'));
  }

  get isOpen() {
    return this.breaker.opened;
  }

  estimate({ origin, destination, serviceLevel, departAt, via }) {
    return this.breaker.fire({ origin, destination, serviceLevel, departAt, via });
  }

  async callWithRetry(request) {
    for (let attempt = 0; ; attempt++) {
      try {
        const res = await this.fetch(`${this.baseUrl}/api/eta/estimate`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(request),
          signal: AbortSignal.timeout(this.timeoutMs),
        });
        if (res.status >= 500) throw new Error(`eta responded ${res.status}`);
        if (!res.ok) {
          // 4xx: our fault, don't retry
          this.log?.warn({ status: res.status }, 'eta rejected the request');
          return null;
        }
        return await res.json();
      } catch (err) {
        if (attempt >= this.retries) throw err;
        await new Promise((r) => setTimeout(r, 100 * 2 ** attempt));
      }
    }
  }

  shutdown() {
    this.breaker.shutdown();
  }
}

module.exports = { EtaClient };
