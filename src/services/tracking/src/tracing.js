'use strict';

// loaded with node -r so it runs before anything else is required
const { NodeSDK } = require('@opentelemetry/sdk-node');
const { OTLPTraceExporter } = require('@opentelemetry/exporter-trace-otlp-http');
const { HttpInstrumentation } = require('@opentelemetry/instrumentation-http');
const { FastifyInstrumentation } = require('@opentelemetry/instrumentation-fastify');
const { PgInstrumentation } = require('@opentelemetry/instrumentation-pg');
const { IORedisInstrumentation } = require('@opentelemetry/instrumentation-ioredis');

if (process.env.OTEL_SDK_DISABLED !== 'true') {
  const sdk = new NodeSDK({
    serviceName: process.env.OTEL_SERVICE_NAME ?? 'tracking',
    // endpoint comes from OTEL_EXPORTER_OTLP_ENDPOINT (http://jaeger:4318 in compose)
    traceExporter: new OTLPTraceExporter(),
    instrumentations: [
      new HttpInstrumentation({ ignoreIncomingRequestHook: (req) => req.url?.startsWith('/health') ?? false }),
      new FastifyInstrumentation(),
      new PgInstrumentation(),
      new IORedisInstrumentation(),
    ],
  });
  sdk.start();

  const shutdown = () => sdk.shutdown().catch(() => {});
  process.once('SIGTERM', shutdown);
  process.once('SIGINT', shutdown);
}
