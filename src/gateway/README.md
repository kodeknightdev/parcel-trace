# gateway (.NET 8, YARP 2.1)

Single entry point for the API. Validates the JWT, applies rate limits and proxies to the services.
Services are never exposed directly (only the gateway and the frontend have an Ingress).

**API Composition:** `GET /api/track/{trackingNumber}` (anonymous, `public` rate limit) is answered
by the gateway itself (`Composition/TrackingComposition.cs`): it asks shipments
(`/api/shipments/track/{tn}`) and tracking (`/api/tracking/{tn}`) in parallel, then eta
(`POST /api/eta/estimate`) for a live estimate from the courier's position, and merges them. Each
call has the standard resilience handler (2 s per attempt, 5 s total, circuit breaker); a missing
source degrades the answer (`sources` says which) instead of failing it.

| Route | Cluster | Auth | Rate limit |
|---|---|---|---|
| `POST /api/auth/login`, `POST /api/auth/register` | identity | anonymous | `login` 10/min per IP |
| `/api/auth/**`, `/api/users/**` | identity | signed in | `api` |
| `POST /api/shipments/quote`, `GET /api/shipments/track/{tn}` | shipments | anonymous | `public` 60/min per IP |
| `/api/shipments/**` | shipments | signed in | `api` |
| `/api/couriers/**`, `/api/dispatch/**` | dispatch | signed in | `api` |
| `GET /api/track/{trackingNumber}` | composed in the gateway | anonymous | `public` |
| `GET /api/tracking/{trackingNumber}` | tracking | anonymous | `public` |
| `/api/tracking/**` | tracking | signed in | `api` |
| `/api/eta/**` | eta | anonymous | `public` |

`api` is a token bucket of 300 requests/min per user. Role checks stay in the services (defence in
depth); the gateway only decides "signed in or not". The original `Authorization` header and the
W3C `traceparent` are forwarded, so traces run end to end.

Swagger UI at `/swagger` lists the gateway's own document (the composed endpoint) and every
service's OpenAPI document, proxied through the `swagger-*` routes. `/health/ready` checks every downstream `/health`.

```bash
dotnet run --project src/gateway/ParcelTrace.Gateway   # http://localhost:8080/swagger
```

Downstream addresses: `ReverseProxy__Clusters__<name>__Destinations__d1__Address`.
