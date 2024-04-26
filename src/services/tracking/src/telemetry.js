'use strict';

const { context, propagation, trace, SpanKind, SpanStatusCode, ROOT_CONTEXT } = require('@opentelemetry/api');

const tracer = trace.getTracer('parcel-trace-tracking');

function injectHeaders(headers = {}) {
  propagation.inject(context.active(), headers);
  return headers;
}

function extractContext(kafkaHeaders = {}) {
  const carrier = {};
  for (const [key, value] of Object.entries(kafkaHeaders)) {
    if (value !== undefined && value !== null) carrier[key] = Buffer.isBuffer(value) ? value.toString('utf8') : String(value);
  }
  return propagation.extract(ROOT_CONTEXT, carrier);
}

async function withSpan(name, options, parentContext, fn) {
  return tracer.startActiveSpan(name, options, parentContext ?? context.active(), async (span) => {
    try {
      return await fn(span);
    } catch (err) {
      span.recordException(err);
      span.setStatus({ code: SpanStatusCode.ERROR, message: err.message });
      throw err;
    } finally {
      span.end();
    }
  });
}

function traceFields() {
  const span = trace.getSpan(context.active());
  if (!span) return {};
  const { traceId, spanId } = span.spanContext();
  return { traceId, spanId };
}

module.exports = { tracer, injectHeaders, extractContext, withSpan, traceFields, SpanKind };
