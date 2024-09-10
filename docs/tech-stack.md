# Tech stack

Every version is pinned and was released before the commit that introduced it. The pins live in
these places:

| Pinned in | What |
|---|---|
| [global.json](../global.json) | .NET SDK |
| [Directory.Packages.props](../Directory.Packages.props) | NuGet packages (Central Package Management) |
| [package-lock.json](../src/services/tracking/package-lock.json) | Node packages |
| [requirements.txt](../src/services/eta/requirements.txt) | Python packages |
| Dockerfiles and compose | image tags |
| `required_providers` + [.terraform.lock.hcl](../infra/terraform/envs/dev/.terraform.lock.hcl) | Terraform providers |

## Runtimes and frameworks

| Technology | Version | Why |
|---|---|---|
| .NET SDK / ASP.NET Core / C# | 8.0 (LTS; `global.json` 8.0.100, roll forward within 8.0), C# 12 | main stack; LTS support, Minimal APIs, built-in rate limiting and `TimeProvider` |
| EF Core + Npgsql provider | 8.0.0 | migrations, owned types for addresses, `xmin` concurrency, raw SQL for `SKIP LOCKED` |
| MongoDB.Driver | 2.23.1 | dispatch documents, conditional updates, TTL and partial indexes |
| Grpc.AspNetCore / Grpc.Net.ClientFactory / Grpc.Tools | 2.60.0 | typed, fast internal lookup dispatch → shipments |
| YARP | 2.1.0 | config-driven reverse proxy in-process with ASP.NET Core auth and rate limiting |
| Razor Pages | ASP.NET Core 8 | server-rendered UI, cookie auth, no separate SPA build |
| Node.js / Fastify | 20.12 LTS / 4.26.2 | tracking: many small high-frequency requests (pings), JSON-schema validation built in |
| kafkajs / ioredis / pg | 2.2.4 / 5.4.1 / 8.11.5 | Kafka, Redis and Postgres clients for Node |
| opossum | 8.1.3 | circuit breaker for tracking → eta |
| Python / FastAPI / pydantic / uvicorn | 3.12.3 / 0.110.3 / 2.7.1 / 0.29.0 | eta: a numeric model, quick to iterate on, typed request models |
| confluent-kafka (Python) | 2.3.0 | librdkafka-based client, same semantics as Confluent.Kafka |

## Libraries (.NET)

| Library | Version | Why |
|---|---|---|
| Confluent.Kafka | 2.3.0 | idempotent producers, manual offset commits |
| Microsoft.Extensions.Http.Resilience | 8.0.0 | standard retry / timeout / circuit-breaker pipeline for HttpClient and gRPC |
| Serilog.AspNetCore | 8.0.0 | structured JSON logs with trace ids |
| FluentValidation | 11.9.0 | request validation for Minimal APIs |
| Swashbuckle.AspNetCore | 6.5.0 | OpenAPI documents and UI |
| AspNetCore.HealthChecks.NpgSql / MongoDb / Kafka | 8.0.0 | readiness checks |
| OpenTelemetry (.NET) | 1.9.0 | tracing for ASP.NET Core, HttpClient (incl. gRPC) and manual Kafka spans |
| BCrypt.Net-Next | 4.0.3 | password hashing |

## Data and messaging

| Technology | Version | Why |
|---|---|---|
| Apache Kafka (KRaft) | 3.7.0 | event backbone; KRaft means no ZooKeeper to run |
| Kafka UI | provectuslabs/kafka-ui v0.7.2 | browsing topics, consumer lag and DLQs |
| PostgreSQL | 16.3 (tests: 16.2) | identity, shipments, tracking |
| MongoDB | 7.0.8 | dispatch |
| Redis | 7.2.4 | positions, ETA cache, dispatcher board |

## Observability

| Technology | Version | Why |
|---|---|---|
| OpenTelemetry SDKs (.NET 1.9.0, Node sdk 0.50.0, Python 1.24.0) | see left | one tracing model for three languages |
| Jaeger all-in-one | 1.57.0 | OTLP-native trace UI for local runs |

## Frontend assets

| Technology | Version | Why |
|---|---|---|
| Bootstrap / Bootstrap Icons | 5.3.3 / 1.11.3 | layout and components without a build step |
| Leaflet + OpenStreetMap tiles | 1.9.4 | maps without API keys |

## Tests

| Technology | Version | Why |
|---|---|---|
| xUnit / FluentAssertions / NSubstitute | 2.6.6 / 6.12.0 / 5.1.0 | .NET unit and page-model tests |
| Testcontainers for .NET | 3.7.0 | real Postgres and MongoDB in integration tests |
| vitest | 1.5.0 | Node tests |
| pytest / httpx | 8.2.0 / 0.27.0 | Python tests and FastAPI TestClient |

## Infrastructure and delivery

| Technology | Version | Why |
|---|---|---|
| Docker images | `mcr.microsoft.com/dotnet/aspnet:8.0.4-bookworm-slim`, `sdk:8.0.204`, `node:20.12.2-alpine3.19`, `python:3.12.3-slim-bookworm` | small, pinned, non-root runtime images |
| Docker Compose | v2 | the whole stack locally |
| Helm | 3.15 | one umbrella chart, a reusable service template, values for local clusters, AKS dev and AKS prod |
| Bitnami charts (dev only) | postgresql 15.5.0, mongodb 15.6.0, redis 19.5.0, kafka 29.3.0 | throwaway datastores in the dev cluster |
| Terraform / azurerm / random (optional) | 1.9.x / 3.110.0 / 3.6.2 | one way to create AKS, ACR, Postgres Flexible Server, Key Vault and Log Analytics; nothing depends on it |
| Azure DevOps Pipelines | multi-stage YAML with templates | build, images, infra plan, gated deploys |
