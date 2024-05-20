'use strict';

const { TrackingService } = require('../src/trackingService');
const { buildHandlers } = require('../src/consumers');
const { LocationStore } = require('../src/locationStore');
const { EtaCache } = require('../src/etaCache');
const { DispatchBoard } = require('../src/readModels');
const { InMemoryRepo, FakeRedis, FakePublisher, FakeEta, testConfig } = require('./fakes');

function createHarness({ now = () => new Date('2024-04-22T12:00:00Z'), etaResult = null } = {}) {
  const repo = new InMemoryRepo();
  const redis = new FakeRedis();
  const publisher = new FakePublisher();
  const eta = new FakeEta(etaResult);
  const locations = new LocationStore(redis, repo);
  const etaCache = new EtaCache(redis, repo);
  const board = new DispatchBoard(redis);
  const service = new TrackingService({ repo, locations, etaCache, board, publisher, eta, config: testConfig, now });
  return { repo, redis, publisher, eta, locations, etaCache, board, service, handlers: buildHandlers(service, repo) };
}

module.exports = { createHarness };
