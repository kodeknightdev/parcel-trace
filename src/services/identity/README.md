# identity (.NET 8, PostgreSQL)

Users, roles and JWTs. Issues HS256 access tokens that every other service validates with the same
issuer, audience and key. Passwords are hashed with BCrypt (enhanced, work factor 11).

Roles: `Customer`, `Dispatcher`, `Courier`, `Admin`. Courier logins carry a `courier_id` claim that
points at the courier record in the dispatch service.

## Endpoints

| Method | Path | Who | |
|---|---|---|---|
| POST | `/api/auth/register` | anyone | create a customer account, returns a token |
| POST | `/api/auth/login` | anyone | email + password → `{ accessToken, expiresAt, user }` |
| GET | `/api/auth/me` | signed in | the current user |
| GET | `/api/users?role=` | Admin | list users |
| POST | `/api/users` | Admin | create a staff/courier/customer account |
| POST | `/api/users/{id}/deactivate` · `/activate` | Admin | block / unblock sign-in |
| GET | `/health`, `/health/ready` | – | liveness / readiness (Postgres) |

Token claims: `sub`, `email`, `name`, `role`, `courier_id` (couriers only), `jti`.

## Kafka

None; identity is purely synchronous.

## Data

Table `users` (EF Core migrations in `Data/Migrations`). In Development the service migrates and
seeds **local demo accounts** on start: `admin@`, `dispatcher@`, `customer@`, `customer2@` and
`courier1@` … `courier4@parceltrace.local`. Their password is the `SEED_DEMO_PASSWORD` setting
(`Demo@12345` in `.env.example`); when it isn't set, no demo accounts are created.

## Run

```bash
dotnet run --project src/services/identity/ParcelTrace.Identity   # http://localhost:5101/swagger
```

Needs Postgres (`ConnectionStrings__Identity`), `Jwt__SigningKey` (set in `appsettings.Development.json`) and,
for the demo accounts, `SEED_DEMO_PASSWORD=Demo@12345` in the environment.
