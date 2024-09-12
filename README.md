# parcel-trace

parcel-trace is a courier and logistics platform built as a microservices monorepo. Customers book
parcels and get a tracking number, dispatchers get couriers onto them (automatically, through
offers the nearest courier accepts, or by hand), couriers stream their position and report
progress, and anyone with a tracking number can follow a parcel live on a map. It is meant for
developers who want a realistic, runnable example of .NET microservices with Kafka, sagas,
an outbox, CQRS and the DevOps around them.

## Features

**Customers**
- Price quotes from distance, weight, size and service level (`Standard` 48 h, `Express` 8 h)
- Booking with a map picker, a `PT-2024-XXXXXX` tracking number and a 30-minute cancel window
- Shipment list with status, ETA and proof of delivery

**Dispatch**
- Courier registry: vehicle, capacity, availability, home base
- Assignment saga: offer to the nearest courier → accept within 2 minutes → next courier → escalate
- Direct assignment, daily routes, a live dashboard with delays and escalations

**Couriers**
- Accept or decline offers, see the day's route
- Location pings, status updates and proof of delivery (recipient, typed signature, photo URL)

**Public tracking**
- Status timeline, live ETA and the courier's last position on a Leaflet / OpenStreetMap map

**Platform**
- JWT roles (`Customer`, `Dispatcher`, `Courier`, `Admin`), API gateway with rate limiting
- Transactional outbox, idempotent consumers, retry and dead-letter topics
- Tracing across HTTP, gRPC and Kafka in Jaeger; health checks everywhere
- Docker Compose locally; Helm for any Kubernetes cluster or AKS; optional Terraform for Azure; Azure DevOps pipeline

## Tech stack

.NET 8 · ASP.NET Core Razor Pages · YARP · gRPC · Kafka (KRaft) · PostgreSQL · MongoDB · Redis ·
Node 20 / Fastify · Python 3.12 / FastAPI · OpenTelemetry / Jaeger · Docker · Helm/Kubernetes · Terraform · Azure DevOps

## Getting started

Prerequisites: Docker with Compose v2 (give it about 6 GB of memory), and Node 20 for the courier simulator.

```bash
cp .env.example .env
docker compose up --build
```

The first build takes a few minutes. Then:

| What | URL |
|---|---|
| Web app | http://localhost:8000 |
| API docs (gateway Swagger, every service) | http://localhost:8080/swagger |
| Kafka UI | http://localhost:8085 |
| Jaeger (traces) | http://localhost:16686 |

Let simulated couriers accept offers and deliver the seeded parcels:

```bash
cd tools/courier-simulator && node simulator.js --speed 300 --interval 1 --once
```

To run on a local Kubernetes cluster (kind, Docker Desktop) instead, use the Helm chart with
`values-local.yaml`; see `deploy/helm/parcel-trace/README.md`. Terraform is only needed to create
the Azure resources and is optional.

## Seed data and demo logins

In Development the services seed themselves on start: identity creates the demo accounts below,
dispatch registers four Berlin couriers (van, car, scooter, and a bike that starts off duty), and
shipments books six demo parcels for the two customers, which dispatch immediately starts offering
to couriers.

These are **local demo accounts only**, created by the Development seeder:

| Role | Email |
|---|---|
| Customer | `customer@parceltrace.local` |
| Dispatcher | `dispatcher@parceltrace.local` |
| Courier | `courier1@parceltrace.local` |
| Admin | `admin@parceltrace.local` |

All demo accounts use the password `Demo@12345`. It comes from `SEED_DEMO_PASSWORD` in
`.env.example`, is used only by the Development seeder, and is local demo data, not a real
credential. The seeder also creates `customer2@` and `courier2@` … `courier4@parceltrace.local`.

## Running tests

```bash
dotnet test ParcelTrace.sln    # unit, page-model and Testcontainers integration tests (Docker needed)
cd src/services/tracking && npm ci && npm test; cd ../eta && pip install -r requirements.txt && pytest
```
