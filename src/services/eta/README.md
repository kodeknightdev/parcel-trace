# eta (Python 3.12, FastAPI)

Stateless ETA estimation:

```
road distance = great-circle distance × 1.3
driving time  = road distance / average speed (Standard 24 km/h, Express 30 km/h) × traffic factor
total         = driving time + handling (Standard 12 min, Express 6 min, +5 min for a pick-up stop)
```

The traffic factor depends on the local hour (`TRAFFIC_TIMEZONE`, default Europe/Berlin), from
0.85 at night to 1.65 in the evening rush (`GET /api/eta/traffic`).

## Endpoints

| Method | Path | |
|---|---|---|
| POST | `/api/eta/estimate` | `{ origin, destination, serviceLevel, departAt?, via? }` → `{ distanceKm, minutes, etaAt, trafficFactor, avgSpeedKmh }` |
| GET | `/api/eta/traffic` | the hourly factors |
| GET | `/health`, `/health/ready` | liveness / Kafka consumer running |
| GET | `/docs`, `/openapi.json` | Swagger UI and OpenAPI |

Called over REST by shipments (initial ETA) and tracking (refreshing a stale ETA).

## Kafka (confluent-kafka)

Consumes `tracking.location-updated`; for every open shipment in the ping it publishes
`eta.recalculated` (via the pick-up while the courier still has to collect the parcel).
Failures go to `eta.retry`, then `eta.dlq`. Trace context is continued from the `traceparent` header.
The service has no database, so the idempotent consumer keeps processed event ids in a bounded
in-memory LRU (`app/idempotency.py`); after a restart a replay just recomputes the same ETA, which
consumers ignore unless it is newer.

## Run

```bash
cd src/services/eta
python3.12 -m venv .venv && . .venv/bin/activate
pip install -r requirements.txt
pytest
KAFKA_ENABLED=false uvicorn app.main:app --port 5105 --reload
```

Dependencies are pinned in `requirements.txt`, compiled from `requirements.in`.
