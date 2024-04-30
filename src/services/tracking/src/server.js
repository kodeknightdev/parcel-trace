'use strict';

const Redis = require('ioredis');
const { loadConfig } = require('./config');
const { createPool, migrate } = require('./db');
const { TrackingRepository } = require('./repository');
const { LocationStore } = require('./locationStore');
const { EtaCache } = require('./etaCache');
const { DispatchBoard } = require('./readModels');
const { TrackingService } = require('./trackingService');
const { createKafka, KafkaPublisher, KafkaConsumer, NullPublisher } = require('./kafka');
const { buildHandlers } = require('./consumers');
const { buildApp, readinessChecks } = require('./app');

async function main() {
  const config = loadConfig();
  if (config.jwt.secret.length < 32) throw new Error('JWT_SIGNING_KEY must be set (32+ characters)');

  const pool = createPool(config.databaseUrl);
  const redis = new Redis(config.redisUrl, { maxRetriesPerRequest: 3 });
  const repo = new TrackingRepository(pool);
  const locations = new LocationStore(redis, repo);
  const etaCache = new EtaCache(redis, repo);
  const board = new DispatchBoard(redis);

  const deps = { config, repo, locations };
  const kafka = config.kafka.enabled ? createKafka(config) : null;
  let publisher;
  let consumer;

  const readiness = readinessChecks({
    postgres: () => repo.ping(),
    redis: () => locations.ping(),
    kafka: async () => {
      if (!kafka) return;
      if (!publisher.connected || !consumer?.isRunning) throw new Error('not connected');
    },
  });

  // logger comes from fastify, so wire these after buildApp
  const service = new TrackingService({ ...deps, etaCache, board, publisher: { publish: (...args) => publisher.publish(...args) } });
  const app = await buildApp({ ...deps, service, readiness });
  service.log = app.log;

  await migrate(pool, app.log);

  if (kafka) {
    publisher = new KafkaPublisher(kafka, app.log);
    await publisher.connect();
    consumer = new KafkaConsumer(kafka, config, buildHandlers(service, repo), app.log);
    await consumer.start();
  } else {
    publisher = new NullPublisher(app.log);
    app.log.warn('Kafka disabled');
  }

  const close = async () => {
    app.log.info('shutting down');
    await app.close();
    await consumer?.stop().catch(() => {});
    await publisher?.disconnect?.().catch(() => {});
    await redis.quit().catch(() => {});
    await pool.end().catch(() => {});
    process.exit(0);
  };
  process.once('SIGTERM', close);
  process.once('SIGINT', close);

  await app.listen({ port: config.port, host: config.host });
}

main().catch((err) => {
  // eslint-disable-next-line no-console
  console.error(err);
  process.exit(1);
});
