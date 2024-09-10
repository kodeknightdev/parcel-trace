# Observability

## Tracing

Every service exports OpenTelemetry traces over OTLP to Jaeger (http://localhost:16686 locally).

| Service | Instrumentation |
|---|---|
| .NET services | ASP.NET Core and HttpClient instrumentation (this covers gRPC client calls too), plus Kafka publish and process spans ([TelemetryExtensions.cs](../shared/ParcelTrace.BuildingBlocks/Telemetry/TelemetryExtensions.cs)) |
| tracking (Node) | HTTP, Fastify, pg and ioredis auto-instrumentation ([tracing.js](../src/services/tracking/src/tracing.js)) and manual Kafka spans ([telemetry.js](../src/services/tracking/src/telemetry.js)) |
| eta (Python) | FastAPI instrumentation and manual Kafka spans ([telemetry.py](../src/services/eta/app/telemetry.py)) |

Kafka has no automatic propagation here, so producers write the W3C `traceparent` and
`tracestate` headers and consumers continue the trace from them
([KafkaTracing.cs](../shared/ParcelTrace.BuildingBlocks/Kafka/KafkaTracing.cs)). The shipments
outbox stores the request's `traceparent` with each row, so a trace continues even though the
event is published later by a background relay.

### Finding a request in Jaeger

1. Do something in the app, for example book a parcel or open a tracking page.
2. Every error response (problem details) carries a `traceId`. Every log line carries `TraceId`
   and `SpanId`.
3. In Jaeger, paste the trace id into the search box, or pick service `gateway` and an operation
   such as `POST /api/shipments`.
4. Open the trace and follow the spans.

### Example: a courier ping

One `POST /api/tracking/pings` produces a single trace across five services:

```
gateway        POST /api/tracking/pings
└─ tracking    POST /api/tracking/pings
   ├─ redis / pg       cache and history writes
   └─ tracking.location-updated publish
      ├─ eta       tracking.location-updated process
      │  └─ eta.recalculated publish
      │     ├─ tracking   eta.recalculated process
      │     └─ shipments  eta.recalculated process
      └─ dispatch  tracking.location-updated process
```

### Example: booking a parcel

`POST /api/shipments` (gateway → shipments) contains the shipments → eta REST call. It continues
with the outbox relay's `shipments.created publish` span, then `shipments.created process` in
dispatch (with its gRPC `GetShipment` call back to shipments) and in tracking.

## Logging

All services write structured JSON to stdout, one event per line:

| Service | Logger |
|---|---|
| .NET | Serilog compact JSON with `Service`, `TraceId`, `SpanId` ([LoggingExtensions.cs](../shared/ParcelTrace.BuildingBlocks/Logging/LoggingExtensions.cs)); `Logging__Format=text` gives readable lines in development |
| Node | pino with a mixin that adds `traceId` / `spanId` |
| Python | a JSON formatter with `TraceId` / `SpanId` ([logging_config.py](../src/services/eta/app/logging_config.py)) |

**Aggregation:**
- locally: `docker compose logs -f <service…>`
- on AKS: Container Insights sends every container's stdout to a Log Analytics workspace (the optional Terraform creates one; otherwise use an existing workspace)
- on any other cluster: `kubectl logs` or whatever log collector the cluster runs

Query all services there, and join to traces by trace id:

```kusto
ContainerLogV2            // or ContainerLog on the legacy schema
| where LogMessage has "PT-2024-K7M2QX" or LogMessage has "<trace id>"
| project TimeGenerated, ContainerName, LogMessage
| order by TimeGenerated asc
```

## Health checks

| Endpoint | Meaning |
|---|---|
| `/health` | liveness: the process is up; no dependencies are checked |
| `/health/ready` | readiness: Postgres / MongoDB / Redis / Kafka as relevant; for the gateway, every downstream service |

Docker Compose health checks and Kubernetes probes use these. The response is JSON listing each
check's status, so `curl localhost:8080/health/ready` shows at a glance which dependency is down.

## Kafka

Kafka UI (http://localhost:8085) shows topics, consumer-group lag and the `*.dlq` topics. A
non-empty DLQ means a message failed every retry. Its `error`, `retry-count` and `original-topic`
headers say why and where it came from.
