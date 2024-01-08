# parcel-trace

Shipment tracking and dispatch for a city courier: customers book parcels, dispatchers assign
couriers, couriers report progress and anyone can follow a parcel by its tracking number.

.NET 8 microservices with a Node tracking service and a Python ETA service, talking over REST,
gRPC and Kafka.

Work in progress.

```bash
dotnet build ParcelTrace.sln
dotnet test ParcelTrace.sln
```
