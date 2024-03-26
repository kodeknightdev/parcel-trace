# shipments (.NET 8, PostgreSQL, gRPC server, outbox)

Owns the shipment lifecycle: quotes, booking, tracking numbers (`PT-2024-XXXXXX`), the cancel
window, SLA and delay detection, and the authoritative status (applied from dispatch and tracking
events). Every state change and its event are written in one transaction through a
**transactional outbox**.

## Endpoints

| Method | Path | Who | |
|---|---|---|---|
| POST | `/api/shipments/quote` | anyone | price for pick-up/drop-off, weight, size, service level |
| POST | `/api/shipments` | Customer, Admin | book; returns the shipment with its tracking number |
| GET | `/api/shipments/mine` | Customer | own shipments |
| GET | `/api/shipments/{id}` | owner, assigned courier, Dispatcher | details incl. proof of delivery |
| POST | `/api/shipments/{id}/cancel` | owner, Dispatcher | within `CancelWindowMinutes` and before pick-up |
| GET | `/api/shipments?status=&unassigned=&courierId=&q=` | Dispatcher | paged list |
| GET | `/api/shipments/track/{trackingNumber}` | anyone | public summary (no names or street addresses) |
| GET | `/api/shipments/dashboard/daily` | Dispatcher | today's counts |
| GET | `/api/shipments/dashboard/delayed` | Dispatcher | open shipments past or projected past their SLA |

**gRPC** (`shared/protos/shipments.proto`, HTTP/2-only port 8081 in containers, 5112 locally):
`ShipmentLookup.GetShipment`, `ShipmentLookup.GetShipments`. Used by dispatch.

**REST client**: calls `eta` (`POST /api/eta/estimate`) for the initial ETA, with the standard
resilience handler (retry, 2 s attempt timeout, circuit breaker). Booking works without it.

## Kafka

| Produces (via outbox) | Consumes |
|---|---|
| `shipments.created` | `dispatch.courier-assigned` → Assigned, courier name |
| `shipments.cancelled` | `tracking.status-changed` → PickedUp … Delivered (with proof) / Failed |
| | `eta.recalculated` → latest ETA |

Consumers are idempotent (`processed_messages`, plus the state machine ignores duplicates and stale
events); an event that arrives too early (e.g. PickedUp before the assignment) is retried through
`shipments.retry`, then parked on `shipments.dlq`.

## Pricing

Base fee + distance (haversine × 1.25 road factor) + weight above 2 kg (billable weight is the
larger of actual and volumetric `L×W×H/5000`, rounded up to 0.5 kg) + 6% fuel surcharge, with a
minimum per service level. All numbers live in the `Pricing` config section.

## Run

```bash
dotnet run --project src/services/shipments/ParcelTrace.Shipments   # http://localhost:5102/swagger
```
