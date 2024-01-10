namespace ParcelTrace.Contracts;

public sealed record GeoPoint(double Lat, double Lng);

public sealed record AddressSummary(string City, string PostalCode, double Lat, double Lng);

public sealed record ShipmentCreated(
    Guid ShipmentId,
    string TrackingNumber,
    Guid CustomerId,
    ServiceLevel ServiceLevel,
    AddressSummary Pickup,
    AddressSummary Dropoff,
    decimal WeightKg,
    DateTimeOffset SlaDueAt,
    DateTimeOffset? EtaAt,
    DateTimeOffset CreatedAt);

public sealed record ShipmentCancelled(
    Guid ShipmentId,
    string TrackingNumber,
    Guid? CourierId,
    string Reason,
    DateTimeOffset CancelledAt);

public sealed record CourierAssigned(
    Guid ShipmentId,
    string TrackingNumber,
    Guid CourierId,
    string CourierName,
    AssignmentMode Mode,
    DateTimeOffset AssignedAt);

public sealed record ActiveShipmentRef(
    Guid ShipmentId,
    string TrackingNumber,
    ShipmentStatus Status,
    ServiceLevel ServiceLevel,
    GeoPoint Pickup,
    GeoPoint Destination);

public sealed record LocationUpdated(
    Guid CourierId,
    double Lat,
    double Lng,
    double? SpeedKmh,
    DateTimeOffset RecordedAt,
    IReadOnlyList<ActiveShipmentRef> Shipments);

public sealed record ProofOfDelivery(
    string RecipientName,
    string SignatureName,
    string? PhotoUrl,
    DateTimeOffset DeliveredAt);

public sealed record StatusChanged(
    Guid ShipmentId,
    string TrackingNumber,
    Guid? CourierId,
    ShipmentStatus Status,
    string? Note,
    ProofOfDelivery? ProofOfDelivery,
    DateTimeOffset OccurredAt);

public sealed record EtaRecalculated(
    Guid ShipmentId,
    string TrackingNumber,
    DateTimeOffset EtaAt,
    int MinutesRemaining,
    double DistanceKm,
    double TrafficFactor,
    DateTimeOffset CalculatedAt);
