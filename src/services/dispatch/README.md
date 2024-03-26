# dispatch (.NET 8, MongoDB, gRPC client)

Courier registry, the assignment queue, the **courier assignment saga** (orchestrated), manual
assignment by dispatchers, and couriers' daily routes.

**Assignment saga** (`Domain/AssignmentSaga.cs`, driven by `Services/AssignmentService.cs`, state in
the `assignment_sagas` collection): a new shipment is **offered** to the best courier and that
courier's capacity is held. The courier **accepts** (→ assignment + `dispatch.courier-assigned`) or
**rejects**; an offer nobody answers **times out** after 2 minutes (`AssignmentSagaWorker`). Each
rejection or timeout releases the hold and offers the shipment to the next courier; after **3**
failures the saga **escalates** to a dispatcher, who assigns directly. Saga documents are saved with
optimistic concurrency (`Version`), so a late accept and a timeout can't both win.

**Choosing a courier** (`Domain/AutoAssigner.cs`): lowest `distance to pick-up + 1.5 km × parcels
already carried` among couriers who are on duty, have a free slot and spare capacity, are within
range for their vehicle (bike 6 km, scooter 15 km, car/van 60 km) and haven't been offered this
shipment yet. Position is the last ping if under 30 minutes old, otherwise the home base. Capacity
is reserved with an optimistic Mongo update, so parallel offers can't overfill a courier.

## Endpoints

| Method | Path | Who | |
|---|---|---|---|
| GET | `/api/couriers` | Dispatcher | registry with live load and availability |
| POST | `/api/couriers` · PUT `/api/couriers/{id}` | Dispatcher | register / edit a courier |
| PUT | `/api/couriers/{id}/availability` | Dispatcher | on/off duty |
| GET | `/api/couriers/me` · PUT `/api/couriers/me/availability` | Courier | own record, go on/off duty |
| GET | `/api/dispatch/offers/me` | Courier | offers waiting for my answer |
| POST | `/api/dispatch/offers/{shipmentId}/accept` · `/reject` | Courier | answer an offer |
| GET | `/api/dispatch/queue` | Dispatcher | waiting shipments (escalated first, then Express) with their offer state |
| GET | `/api/dispatch/escalations` · `/sagas/{shipmentId}` | Dispatcher | escalated sagas, one saga's offer history |
| POST | `/api/dispatch/assign` | Dispatcher | direct assignment (resolves escalations, re-assigns before pick-up) |
| POST | `/api/dispatch/auto-assign` · `/auto-assign/all` | Dispatcher | (re)start offers for waiting shipments |
| GET | `/api/dispatch/routes/{courierId}?date=` | Dispatcher | a courier's stops for a day |
| GET | `/api/dispatch/routes/me?date=` | Courier | own route |
| GET | `/api/dispatch/summary` | Dispatcher | counts for the dashboard |
| GET | `/api/dispatch/assignments/{shipmentId}` | staff | current assignment |

**gRPC client**: `ShipmentLookup.GetShipment` on the shipments service, to read the pick-up address,
weight and current status at assignment time.

## Kafka

| Consumes | Produces |
|---|---|
| `shipments.created` → queue + start the assignment saga | `dispatch.courier-assigned` (on accept or direct assignment) |
| `shipments.cancelled` → compensation: end the saga, drop from queue, free the courier | |
| `tracking.status-changed` → Delivered/Failed frees the courier | |
| `tracking.location-updated` → courier's last position | |

Every event id is recorded in `processed_events` (TTL 7 days) and skipped on redelivery.
`AssignmentSagaWorker` runs every 10 s (`Dispatch:SweepIntervalSeconds`): it times out expired
offers and makes offers for shipments still waiting for a free courier. Timeout and attempts:
`Dispatch:OfferTimeoutSeconds` (120), `Dispatch:MaxOfferAttempts` (3).

## Data

Collections `couriers`, `assignments`, `pending_shipments`, `assignment_sagas`, `processed_events` (TTL 7 days).
Indexes are created at startup, including a partial unique index allowing one active assignment
per shipment. Development seeds four Berlin couriers (ids match the identity seed).

## Run

```bash
dotnet run --project src/services/dispatch/ParcelTrace.Dispatch   # http://localhost:5103/swagger
```
