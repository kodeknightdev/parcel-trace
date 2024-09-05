# ParcelTrace.Contracts

Plain records shared by the .NET services: Kafka topic names, event payloads, the event envelope,
roles and claim names, and a haversine helper. No dependencies on purpose.

The Node (`tracking`) and Python (`eta`) services mirror these shapes by hand, so any change to a
payload here has to be reflected in `src/services/tracking/src/events.js` and
`src/services/eta/app/schemas.py`. Additive changes only; bump `schemaVersion` otherwise.
