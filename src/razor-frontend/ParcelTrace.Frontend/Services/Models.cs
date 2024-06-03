using ParcelTrace.Contracts;

namespace ParcelTrace.Frontend.Services;

public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role, Guid? CourierId, bool IsActive, DateTimeOffset CreatedAt);

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed record CreateUserRequest(string Email, string Password, string DisplayName, string Role, Guid? CourierId);

public sealed record PartyDto(string Name, string Phone, string? Email = null);

public sealed record AddressDto(string Line1, string City, string PostalCode, string Country, double Lat, double Lng);

public sealed record PackageDto(decimal WeightKg, int LengthCm, int WidthCm, int HeightCm);

public sealed record QuoteRequest(GeoPoint Pickup, GeoPoint Dropoff, PackageDto Package, ServiceLevel ServiceLevel);

public sealed record Quote(ServiceLevel ServiceLevel, double DistanceKm, decimal BillableWeightKg, decimal BaseFee, decimal DistanceFee,
    decimal WeightFee, decimal FuelSurcharge, decimal Total, string Currency, int SlaHours);

public sealed record CreateShipmentRequest(PartyDto Sender, PartyDto Recipient, AddressDto Pickup, AddressDto Dropoff, PackageDto Package,
    ServiceLevel ServiceLevel, string? Notes);

public sealed record ProofDto(string RecipientName, string SignatureName, string? PhotoUrl, DateTimeOffset DeliveredAt);

public sealed record ShipmentDto(
    Guid Id, string TrackingNumber, ShipmentStatus Status, ServiceLevel ServiceLevel, Guid CustomerId,
    PartyDto Sender, PartyDto Recipient, AddressDto Pickup, AddressDto Dropoff, PackageDto Package, string? Notes,
    decimal Price, string Currency, double DistanceKm, Guid? CourierId, string? CourierName,
    DateTimeOffset CreatedAt, DateTimeOffset CancelDeadline, bool CanCancel, DateTimeOffset SlaDueAt, DateTimeOffset? EtaAt, bool IsDelayed,
    DateTimeOffset? AssignedAt, DateTimeOffset? PickedUpAt, DateTimeOffset? DeliveredAt, DateTimeOffset? CancelledAt,
    string? CancelReason, string? FailureReason, ProofDto? Proof);

public sealed record DailyCounts(DateOnly Date, int Created, int Delivered, int Failed, int Cancelled, int Unassigned, int InProgress, int Delayed);

public sealed record TimelineEntry(ShipmentStatus Status, string? Note, DateTimeOffset OccurredAt);

public sealed record EtaView(DateTimeOffset EtaAt, int? MinutesRemaining, double? DistanceKm, string Source);

public sealed record LocationView(double Lat, double Lng, DateTimeOffset RecordedAt);

public sealed record DestinationView(string City, string PostalCode, double Lat, double Lng);

public sealed record SourceStatus(string Shipments, string Tracking, string Eta);

public sealed record PublicTracking(
    string TrackingNumber, ShipmentStatus Status, ServiceLevel ServiceLevel, string From, DestinationView? To, string? CourierName,
    DateTimeOffset SlaDueAt, bool IsDelayed, EtaView? Eta, LocationView? LastLocation, DateTimeOffset? DeliveredAt, string? DeliveredTo,
    IReadOnlyList<TimelineEntry> Timeline, SourceStatus Sources);

public sealed record CourierDto(
    Guid Id, string Name, string Phone, VehicleType VehicleType, double CapacityKg, int MaxActiveShipments, bool IsAvailable,
    int ActiveCount, double CurrentLoadKg, GeoPoint HomeBase, GeoPoint? LastLocation, DateTime? LastSeenAt, string Availability);

public sealed record CourierRequest(string Name, string Phone, VehicleType VehicleType, double CapacityKg, int MaxActiveShipments, GeoPoint HomeBase);

public sealed record PendingShipment(Guid ShipmentId, string TrackingNumber, ServiceLevel ServiceLevel, string PickupCity, string PickupPostalCode,
    string DropoffCity, double WeightKg, DateTime SlaDueAt, DateTime QueuedAt, int Attempts, string? LastError,
    string? OfferedTo, DateTime? OfferExpiresAt, bool Escalated);

public sealed record StopDto(string Line1, string City, string PostalCode, double Lat, double Lng);

public sealed record AssignmentDto(
    Guid Id, Guid ShipmentId, string TrackingNumber, Guid CourierId, string CourierName, AssignmentMode Mode, string AssignedBy,
    DateTime AssignedAt, string RouteDate, string Status, ShipmentStatus ShipmentStatus, ServiceLevel ServiceLevel, double WeightKg,
    double DistanceKm, string RecipientName, StopDto Pickup, StopDto Dropoff, DateTime SlaDueAt, DateTime? CompletedAt);

public sealed record RouteDto(Guid CourierId, string CourierName, string Date, IReadOnlyList<AssignmentDto> Stops, int Open, int Done);

public sealed record DispatchSummary(int Couriers, int Available, int Idle, int Busy, int Full, int OffDuty, int Pending, int Offered, int Escalated, int ActiveAssignments);

public sealed record BoardCourier(Guid CourierId, int ActiveShipments, LocationView? Location);

public sealed record BoardToday(int Created, int Assigned, int PickedUp, int Delivered, int Failed, int Cancelled);

public sealed record DispatchBoard(string Day, IReadOnlyDictionary<string, int> ByStatus, BoardToday Today, IReadOnlyList<BoardCourier> Couriers);

public sealed record OfferHistory(Guid CourierId, string CourierName, double DistanceKm, DateTime OfferedAt, DateTime ExpiresAt, string Outcome, DateTime? RespondedAt, string? Reason);

public sealed record AssignmentSaga(Guid ShipmentId, string TrackingNumber, string State, int Failures, IReadOnlyList<OfferHistory> Offers,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? EscalatedAt, string? EscalationReason);

public sealed record CourierOffer(Guid ShipmentId, string TrackingNumber, string CourierName, double DistanceKm, DateTime ExpiresAt,
    ServiceLevel ServiceLevel, string PickupCity, string PickupPostalCode, string DropoffCity, double WeightKg);

public sealed record OfferResult(Guid ShipmentId, string TrackingNumber, bool Offered, string State, Guid? CourierId, string? CourierName, double? DistanceKm, DateTime? ExpiresAt, string? Reason);

public sealed record StatusUpdate(string Status, string? Note = null, ProofInput? Proof = null, double? Lat = null, double? Lng = null);

public sealed record ProofInput(string RecipientName, string SignatureName, string? PhotoUrl);

public sealed record StatusUpdateResult(Guid ShipmentId, string TrackingNumber, ShipmentStatus Status, bool Changed);
