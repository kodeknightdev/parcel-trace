# ParcelTrace.BuildingBlocks

Plumbing shared by the .NET services so each `Program.cs` stays about the domain:

| Folder | What's in it |
|---|---|
| `ServiceDefaults.cs` | `AddServiceDefaults` / `UseServiceDefaults`: logging, tracing, ProblemDetails, Swagger, health checks, validators |
| `Auth/` | JWT bearer validation and the `customer` / `dispatcher` / `courier` / `admin` / `staff` policies |
| `Errors/` | `AppException` family mapped to RFC 7807 by an `IExceptionHandler` |
| `Health/` | `/health` (liveness) and `/health/ready` (checks tagged `ready`) |
| `Kafka/` | publisher, base consumer with retry + DLQ topics, W3C trace propagation through headers |
| `Logging/` | Serilog JSON console output enriched with `TraceId` / `SpanId` |
| `Telemetry/` | OpenTelemetry tracing (ASP.NET Core, HttpClient, Kafka) exported over OTLP |
| `Validation/` | FluentValidation endpoint filter for Minimal APIs |
