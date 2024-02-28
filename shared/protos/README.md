# protos

gRPC contracts. `shipments.proto` is compiled as a server in `ParcelTrace.Shipments` and as a client
in `ParcelTrace.Dispatch` (both reference this file through `<Protobuf Include=... Link=...>`).

The shipments service listens for gRPC on a dedicated HTTP/2-only port (`8081` in containers)
because plaintext h2c needs prior knowledge and can't share the HTTP/1.1 port.
