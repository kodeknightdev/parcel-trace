# Development

## Project structure

```
parcel-trace/
├── src/
│   ├── gateway/ParcelTrace.Gateway/          YARP routes, JWT, rate limiting, /api/track composition, Swagger
│   ├── razor-frontend/ParcelTrace.Frontend/  Razor Pages UI, typed gateway client, cookie auth
│   └── services/
│       ├── identity/                          users, roles, JWT (EF Core + Postgres)
│       ├── shipments/                         lifecycle, quotes, outbox, gRPC server (EF Core + Postgres)
│       ├── dispatch/                          couriers, assignment saga, routes (MongoDB, gRPC client)
│       ├── tracking/                          Node/Fastify: event log, projections, board, caches, pings
│       └── eta/                               Python/FastAPI: ETA model + Kafka worker
├── shared/
│   ├── ParcelTrace.Contracts/                 topics, events, envelope, roles, haversine
│   ├── ParcelTrace.BuildingBlocks/            service defaults: logging, tracing, errors, auth, Kafka, health
│   └── protos/shipments.proto                 gRPC contract
├── tests/                                     .NET test projects (see below)
├── tools/courier-simulator/                   plays the couriers
├── deploy/                                    compose, Kafka topics, Postgres init, Helm chart, pipeline templates
├── infra/terraform/                           optional: Azure modules and dev/prod environments
├── docs/                                      these documents
└── azure-pipelines.yml · ParcelTrace.sln · global.json · Directory.Build.props · Directory.Packages.props
```

Each service folder has a README with its endpoints, events and how to run it alone.

## Running services without Docker

Start only the infrastructure in Docker, then run a service from your IDE or shell. The override
file publishes every datastore and service port on localhost.

```bash
cp .env.example .env
docker compose up -d postgres mongo redis kafka kafka-init jaeger
```

| Service | Command | Notes |
|---|---|---|
| identity | `SEED_DEMO_PASSWORD=Demo@12345 dotnet run --project src/services/identity/ParcelTrace.Identity` | port 5101, creates the demo accounts |
| shipments | `dotnet run --project src/services/shipments/ParcelTrace.Shipments` | REST 5102, gRPC 5112 |
| dispatch | `dotnet run --project src/services/dispatch/ParcelTrace.Dispatch` | port 5103 |
| gateway | `dotnet run --project src/gateway/ParcelTrace.Gateway` | port 8080 |
| frontend | `dotnet run --project src/razor-frontend/ParcelTrace.Frontend` | port 5000 |
| tracking | `cd src/services/tracking && npm ci && JWT_SIGNING_KEY=dev-only-signing-key-change-me-0123456789abcdef npm run dev` | port 5104 |
| eta | `cd src/services/eta && pip install -r requirements.txt && uvicorn app.main:app --port 5105 --reload` | port 5105 |

The .NET services read `appsettings.Development.json` (local ports, the dev JWT key) and migrate
and seed on start.

## Tests

| Where | What | Command |
|---|---|---|
| `tests/ParcelTrace.Shipments.UnitTests` | pricing, state machine, cancel window, tracking numbers | `dotnet test ParcelTrace.sln` |
| `tests/ParcelTrace.Dispatch.UnitTests` | courier ranking, assignment saga rules | ↑ |
| `tests/ParcelTrace.Identity.Tests` | tokens, hashing, validators | ↑ |
| `tests/ParcelTrace.Gateway.Tests` | API composition, degraded paths | ↑ |
| `tests/ParcelTrace.Frontend.Tests` | Razor page models | ↑ |
| `tests/ParcelTrace.Shipments.IntegrationTests` | Testcontainers Postgres: API, outbox, consumers, gRPC | ↑ (needs Docker) |
| `tests/ParcelTrace.Dispatch.IntegrationTests` | Testcontainers MongoDB: offers, timeouts, escalation, compensation, capacity | ↑ (needs Docker) |
| `src/services/tracking/tests` | event log, projections, read models, caches, circuit breaker, routes | `npm test` |
| `src/services/eta/tests` | ETA model, API, events, idempotency | `pytest` |

Without a Docker daemon the Testcontainers tests report as skipped (`[DockerFact]`). Set
`SKIP_DOCKER_TESTS=true` to skip them on purpose.

## Conventions

- **Commits:** conventional commits (`feat(dispatch): …`, `fix(tracking): …`, `docs: …`).
- **Versions:** pinned exactly. NuGet versions go in `Directory.Packages.props` only, never in a `.csproj`.
- **.NET style:** Minimal APIs grouped per area in `Endpoints/`; FluentValidation for request bodies; expected failures as `AppException` subclasses (mapped to problem details); `TimeProvider` instead of `DateTime.Now`.
- **Events:** past-tense names, the shared envelope, keyed by aggregate id. Additive changes only, otherwise bump `schemaVersion`.
- **Consumers:** idempotent. Throw to retry; never swallow a failure you want retried.
- **Line endings and indentation:** set by `.editorconfig`.

## Adding a new service

1. Create `src/services/<name>/` with its own README, Dockerfile and `/health` + `/health/ready`.
2. **.NET:** reference `ParcelTrace.BuildingBlocks`, call `AddServiceDefaults("<name>")` / `UseServiceDefaults()`, and add the project to `ParcelTrace.sln`.
3. Give it its own datastore; never read another service's tables.
4. **Events:** add the topic to `Contracts/Topics.cs` and `deploy/kafka/create-topics.sh`, plus `<group>.retry` / `<group>.dlq`. Make the consumer idempotent.
5. **Deployment wiring:**
   - a service in `deploy/docker-compose.yml`
   - an entry under `services:` in `deploy/helm/parcel-trace/values.yaml`
   - a route and cluster in the gateway's `appsettings.json` if it has a public API
   - a matrix entry in `deploy/pipelines/templates/docker-images.yml`
6. Document it in [architecture.md](architecture.md) and [api.md](api.md).
