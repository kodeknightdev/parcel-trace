# razor-frontend (ASP.NET Core 8 Razor Pages, Bootstrap 5.3, Leaflet 1.9)

The UI for everyone. It talks only to the gateway, through a typed `GatewayClient` with the
standard resilience handler.

Sign-in posts the credentials to `/api/auth/login`; the returned JWT is stored inside the
(HttpOnly) auth cookie together with the user's role, and `AccessTokenHandler` forwards it as a
bearer token on every gateway call. The cookie expires with the token.

| Area | Pages | Role |
|---|---|---|
| Public | `/` (track box), `/Track?tn=` (the gateway's composed view: timeline, ETA, Leaflet map with the courier, polls every 15 s), `/Quote` | anyone |
| Account | `/Account/Login`, `/Account/Register`, `/Account/Logout` | anyone |
| Shipments | `/Shipments` (list), `/Shipments/Create` (map picker, price preview), `/Shipments/Details/{id}` (cancel) | Customer |
| Dispatch | `/Dispatch` (counts from the tracking board read model, queue with offer state and escalations, manual assignment, delayed, couriers), `/Dispatch/Couriers`, `/Dispatch/Route` | Dispatcher |
| Courier | `/Courier` (offers to accept or decline, today's stops, next-step buttons, on/off duty), `/Courier/Deliver/{id}` (proof of delivery or failure) | Courier |
| Admin | `/Admin/Users` | Admin |

Map tiles come from OpenStreetMap.

```bash
dotnet run --project src/razor-frontend/ParcelTrace.Frontend   # http://localhost:5000
```

`Gateway__BaseUrl` points at the gateway (default `http://localhost:8080`).
