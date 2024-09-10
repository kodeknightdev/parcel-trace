# API

All public traffic goes through the gateway at `http://localhost:8080`. Its Swagger UI
(`/swagger`) lists the gateway's own document (the composed endpoint) and every service's
OpenAPI document. Services can also be called directly on their local ports (see
[architecture.md](architecture.md#services)).

Authentication: `POST /api/auth/login` returns a JWT. Send it as `Authorization: Bearer <token>`.
Errors are RFC 7807 problem details with a `traceId`.

## Gateway (composition)

| Method | Path | Who | |
|---|---|---|---|
| GET | `/api/track/{trackingNumber}` | anyone | public tracking composed from shipments, tracking and eta, with `sources` saying what was available |

## identity

| Method | Path | Who | |
|---|---|---|---|
| POST | `/api/auth/register` | anyone | create a customer account |
| POST | `/api/auth/login` | anyone | email + password → `{ accessToken, expiresAt, user }` |
| GET | `/api/auth/me` | signed in | current user |
| GET / POST | `/api/users` | Admin | list / create accounts |
| POST | `/api/users/{id}/deactivate` · `/activate` | Admin | block / unblock sign-in |

## shipments

| Method | Path | Who | |
|---|---|---|---|
| POST | `/api/shipments/quote` | anyone | price for a parcel |
| POST | `/api/shipments` | Customer | book; returns the tracking number |
| GET | `/api/shipments/mine` | Customer | own shipments |
| GET | `/api/shipments/{id}` | owner, assigned courier, Dispatcher | details incl. proof of delivery |
| POST | `/api/shipments/{id}/cancel` | owner, Dispatcher | within the cancel window, before pick-up |
| GET | `/api/shipments` | Dispatcher | filtered, paged list |
| GET | `/api/shipments/track/{trackingNumber}` | anyone | public summary (no personal data) |
| GET | `/api/shipments/dashboard/daily` · `/delayed` | Dispatcher | today's counts, shipments past or projected past the SLA |

**gRPC** ([shared/protos/shipments.proto](../shared/protos/shipments.proto)), HTTP/2-only port
8081 (5112 locally): `ShipmentLookup.GetShipment`, `ShipmentLookup.GetShipments`. Internal only,
used by dispatch.

## dispatch

| Method | Path | Who | |
|---|---|---|---|
| GET / POST / PUT | `/api/couriers`, `/api/couriers/{id}` | Dispatcher | courier registry |
| PUT | `/api/couriers/{id}/availability` | Dispatcher | on/off duty |
| GET · PUT | `/api/couriers/me` · `/api/couriers/me/availability` | Courier | own record, go on/off duty |
| GET | `/api/dispatch/offers/me` | Courier | offers waiting for an answer |
| POST | `/api/dispatch/offers/{shipmentId}/accept` · `/reject` | Courier | answer an offer |
| GET | `/api/dispatch/queue` | Dispatcher | shipments without a courier, with offer state |
| GET | `/api/dispatch/escalations` | Dispatcher | sagas that need a dispatcher |
| GET | `/api/dispatch/sagas/{shipmentId}` | Dispatcher | a saga's offer history |
| POST | `/api/dispatch/assign` | Dispatcher | direct assignment (resolves escalations) |
| POST | `/api/dispatch/auto-assign` · `/auto-assign/all` | Dispatcher | (re)start offers |
| GET | `/api/dispatch/routes/me` · `/routes/{courierId}?date=` | Courier · Dispatcher | a day's route |
| GET | `/api/dispatch/summary` | Dispatcher | couriers, queue, offers, escalations |

## tracking

| Method | Path | Who | |
|---|---|---|---|
| POST | `/api/tracking/pings` | Courier | location ping |
| POST | `/api/tracking/shipments/{id}/status` | assigned Courier, Dispatcher | PickedUp … Delivered (with proof) / Failed |
| GET | `/api/tracking/shipments/{id}/timeline` | owner, courier, staff | event log incl. proof |
| GET | `/api/tracking/{trackingNumber}` | anyone | timeline, ETA, last position |
| GET | `/api/tracking/board` | Dispatcher | dashboard read model |
| GET | `/api/tracking/couriers/locations` · `/couriers/{id}/history` | Dispatcher | positions, pings |
| POST | `/api/tracking/admin/rebuild-projections` | Admin | rebuild read models from the log |

## eta

| Method | Path | Who | |
|---|---|---|---|
| POST | `/api/eta/estimate` | anyone (via gateway) | `{ origin, destination, serviceLevel, departAt?, via? }` → `{ distanceKm, minutes, etaAt, trafficFactor, avgSpeedKmh }` |
| GET | `/api/eta/traffic` | anyone | the hourly traffic factors |

## OpenAPI UIs

| Service | UI | Document |
|---|---|---|
| all, through the gateway | http://localhost:8080/swagger | `/swagger/docs/{service}.json` |
| identity / shipments / dispatch | http://localhost:5101/swagger · 5102 · 5103 | `/swagger/v1/swagger.json` |
| tracking | http://localhost:5104/docs | `/docs/json` |
| eta | http://localhost:5105/docs | `/openapi.json` |
