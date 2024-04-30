'use strict';

const Fastify = require('fastify');
const swagger = require('@fastify/swagger');
const swaggerUi = require('@fastify/swagger-ui');
const auth = require('./auth');
const trackingRoutes = require('./routes/tracking');
const { HttpError } = require('./trackingService');
const { traceFields } = require('./telemetry');

async function buildApp({ config, service, repo, locations, readiness, logger = true }) {
  const app = Fastify({
    logger: logger === true ? { level: config.logLevel, mixin: traceFields } : logger,
    ajv: { customOptions: { coerceTypes: 'array', removeAdditional: true } },
  });

  await app.register(swagger, {
    openapi: {
      info: { title: 'parcel-trace tracking', version: '1.0.0' },
      components: { securitySchemes: { bearer: { type: 'http', scheme: 'bearer', bearerFormat: 'JWT' } } },
      security: [{ bearer: [] }],
    },
  });
  await app.register(swaggerUi, { routePrefix: '/docs' });
  await app.register(auth, config.jwt);

  app.setErrorHandler((err, request, reply) => {
    if (err instanceof HttpError) {
      return reply.code(err.statusCode).type('application/problem+json').send({ type: 'about:blank', title: err.title, status: err.statusCode, detail: err.detail, instance: request.url });
    }
    if (err.validation) {
      return reply.code(400).type('application/problem+json').send({ type: 'about:blank', title: 'Validation failed', status: 400, detail: err.message, instance: request.url });
    }
    request.log.error({ err }, 'unhandled error');
    return reply.code(500).type('application/problem+json').send({ type: 'about:blank', title: 'An unexpected error occurred', status: 500, instance: request.url });
  });

  app.get('/health', { schema: { hide: true } }, async () => ({ status: 'Healthy' }));
  app.get('/health/ready', { schema: { hide: true } }, async (_request, reply) => {
    const checks = await readiness();
    const healthy = checks.every((c) => c.status === 'Healthy');
    return reply.code(healthy ? 200 : 503).send({ status: healthy ? 'Healthy' : 'Unhealthy', checks });
  });

  await app.register(trackingRoutes, { service, repo, locations });
  return app;
}

function readinessChecks(checks) {
  return async () =>
    Promise.all(
      Object.entries(checks).map(async ([name, check]) => {
        try {
          await check();
          return { name, status: 'Healthy' };
        } catch (err) {
          return { name, status: 'Unhealthy', error: err.message };
        }
      }),
    );
}

module.exports = { buildApp, readinessChecks };
