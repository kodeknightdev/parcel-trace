# tracking (Node 20, Fastify 4, PostgreSQL + Redis)

Courier location pings, each shipment's status timeline, courier status updates with proof of
delivery, the dispatcher board and the public tracking view.

## How data is kept

| Data | Where | Pattern |
|---|---|---|
| Shipment timeline | `shipment_events` (Postgres), **append-only** (a trigger rejects UPDATE/DELETE) | Event Sourcing (light): `src/eventStore.js`, `src/projection.js` |
| Current status, courier, reference data | `shipment_view` (Postgres), a projection of the log | rebuilt any time with `npm run rebuild` or `POST /api/tracking/admin/rebuild-projections` |
| Dispatcher board | Redis hashes/sets fed by every appended event | CQRS read model: `src/readModels.js` |
| Latest courier position | Redis, in front of `location_pings` (full history in Postgres) | Cache-Aside: `src/locationStore.js` |
| Latest ETA per shipment | Redis, in front of `eta_estimates` | Cache-Aside: `src/etaCache.js` |

## Endpoints

| Method | Path | Who | |
|---|---|---|---|
| POST | `/api/tracking/pings` | Courier (staff may pass `courierId`) | `{ lat, lng, speedKmh?, recordedAt? }` |
| POST | `/api/tracking/shipments/:id/status` | assigned Courier, Dispatcher | `PickedUp`, `InTransit`, `OutForDelivery`, `Delivered` (needs `proof`), `Failed` (needs `note`) |
| GET | `/api/tracking/shipments/:id/timeline` | owner, courier, staff | the event log incl. proof of delivery |
| GET | `/api/tracking/:trackingNumber` | anyone | timeline, ETA, courier's last position while on the way |
| GET | `/api/tracking/board` | Dispatcher | read model: shipments per status, today's counts, couriers on the road |
| GET | `/api/tracking/couriers/locations` · `/couriers/:id/history` | Dispatcher | positions, recent pings |
| POST | `/api/tracking/admin/rebuild-projections` | Admin | rebuild projections and the board from the log |
| GET | `/docs` | – | Swagger UI (`/docs/json` for the document) |

The public view rounds the destination to ~1 km and shows only the courier's first name. The
gateway's composed `/api/track/{trackingNumber}` builds on it.

Status updates are validated against the same state machine as the shipments service, appended to
the log in a transaction that commits only after Kafka acknowledged `tracking.status-changed`.

**tracking → eta (REST):** a stale ETA on a public lookup is refreshed from eta through
`src/etaClient.js`: per-attempt timeout, retries with backoff, and an **opossum circuit breaker**
that fails fast (falls back to the cached ETA) while eta is down.

## Kafka (kafkajs)

| Consumes | Produces |
|---|---|
| `shipments.created` → `Created` event | `tracking.location-updated` (one per ping, with the courier's open shipments) |
| `dispatch.courier-assigned` → `Assigned` event | `tracking.status-changed` |
| `shipments.cancelled` → `Cancelled` event (closes the timeline) | |
| `eta.recalculated` → ETA store + cache | |

Idempotent consumer: every event id goes into `processed_events`, and the log's unique
`source_event_id` makes a second append impossible anyway. Trace context travels in `traceparent`
headers; failed messages go to `tracking.retry`, then `tracking.dlq`.

## Run

```bash
cd src/services/tracking
npm ci
npm test
JWT_SIGNING_KEY=dev-only-signing-key-change-me-0123456789abcdef npm run dev   # http://localhost:5104/docs
```

Environment: `DATABASE_URL`, `REDIS_URL`, `KAFKA_BROKERS`, `KAFKA_ENABLED`, `JWT_SIGNING_KEY`,
`ETA_URL`, `OTEL_EXPORTER_OTLP_ENDPOINT`. Migrations in `migrations/` run on start.
