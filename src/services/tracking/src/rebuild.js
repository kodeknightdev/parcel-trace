'use strict';

// npm run rebuild - recompute projections from the event log
const Redis = require('ioredis');
const { loadConfig } = require('./config');
const { createPool, migrate } = require('./db');
const { TrackingRepository } = require('./repository');
const { LocationStore } = require('./locationStore');
const { EtaCache } = require('./etaCache');
const { DispatchBoard } = require('./readModels');
const { TrackingService } = require('./trackingService');

async function main() {
  const config = loadConfig();
  const pool = createPool(config.databaseUrl);
  const redis = new Redis(config.redisUrl);
  const repo = new TrackingRepository(pool);
  const log = { info: console.log, warn: console.warn };
  await migrate(pool, log);

  const service = new TrackingService({
    repo,
    locations: new LocationStore(redis, repo),
    etaCache: new EtaCache(redis, repo),
    board: new DispatchBoard(redis),
    config,
    log,
  });
  const result = await service.rebuildProjections();
  console.log(`rebuilt ${result.shipments} shipment projections and the dispatcher board`);
  await redis.quit();
  await pool.end();
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
