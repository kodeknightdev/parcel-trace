'use strict';

const int = (value, fallback) => (value === undefined || value === '' ? fallback : Number.parseInt(value, 10));
const bool = (value, fallback) => (value === undefined || value === '' ? fallback : value === 'true' || value === '1');

function loadConfig(env = process.env) {
  return {
    port: int(env.PORT, 5104),
    host: env.HOST ?? '0.0.0.0',
    logLevel: env.LOG_LEVEL ?? 'info',
    databaseUrl: env.DATABASE_URL ?? 'postgres://parcel:parcel@localhost:5432/tracking',
    redisUrl: env.REDIS_URL ?? 'redis://localhost:6379',
    kafka: {
      enabled: bool(env.KAFKA_ENABLED, true),
      brokers: (env.KAFKA_BROKERS ?? 'localhost:9094').split(',').map((b) => b.trim()),
      clientId: env.KAFKA_CLIENT_ID ?? 'tracking',
      groupId: env.KAFKA_GROUP_ID ?? 'tracking',
      maxRetries: int(env.KAFKA_MAX_RETRIES, 3),
    },
    jwt: {
      secret: env.JWT_SIGNING_KEY ?? '',
      issuer: env.JWT_ISSUER ?? 'parcel-trace-identity',
      audience: env.JWT_AUDIENCE ?? 'parcel-trace',
    },
    eta: {
      baseUrl: env.ETA_URL ?? 'http://localhost:5105',
      timeoutMs: int(env.ETA_TIMEOUT_MS, 2000),
      retries: int(env.ETA_RETRIES, 2),
      staleAfterMs: int(env.ETA_STALE_AFTER_MS, 10 * 60 * 1000),
    },
  };
}

module.exports = { loadConfig };
