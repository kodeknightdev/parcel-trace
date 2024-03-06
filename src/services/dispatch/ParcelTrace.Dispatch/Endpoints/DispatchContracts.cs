using FluentValidation;
using ParcelTrace.Contracts;
using ParcelTrace.Dispatch.Domain;

namespace ParcelTrace.Dispatch.Endpoints;

public sealed record CourierRequest(string Name, string Phone, VehicleType VehicleType, double CapacityKg, int MaxActiveShipments, GeoPoint HomeBase);

public sealed record AvailabilityRequest(bool IsAvailable);

public sealed record AssignRequest(Guid ShipmentId, Guid CourierId);

public sealed record AutoAssignRequest(Guid ShipmentId);


public sealed record CourierDto(
    Guid Id,
    string Name,
    string Phone,
    VehicleType VehicleType,
    double CapacityKg,
    int MaxActiveShipments,
    bool IsAvailable,
    int ActiveCount,
    double CurrentLoadKg,
    GeoPoint HomeBase,
    GeoPoint? LastLocation,
    DateTime? LastSeenAt,
    string Availability)
{
    public static CourierDto From(Courier c) => new(
        c.Id, c.Name, c.Phone, c.VehicleType, c.CapacityKg, c.MaxActiveShipments, c.IsAvailable, c.ActiveCount,
        Math.Round(c.CurrentLoadKg, 2), c.HomeBase.ToPoint(), c.LastLocation?.ToPoint(), c.LastSeenAt,
        !c.IsAvailable ? "OffDuty" : c.ActiveCount >= c.MaxActiveShipments ? "Full" : c.ActiveCount > 0 ? "Busy" : "Idle");
}

public sealed record StopDto(string Line1, string City, string PostalCode, double Lat, double Lng)
{
    public static StopDto From(Stop s) => new(s.Line1, s.City, s.PostalCode, s.Lat, s.Lng);
}

public sealed record AssignmentDto(
    Guid Id,
    Guid ShipmentId,
    string TrackingNumber,
    Guid CourierId,
    string CourierName,
    AssignmentMode Mode,
    string AssignedBy,
    DateTime AssignedAt,
    string RouteDate,
    AssignmentStatus Status,
    ShipmentStatus ShipmentStatus,
    ServiceLevel ServiceLevel,
    double WeightKg,
    double DistanceKm,
    string RecipientName,
    StopDto Pickup,
    StopDto Dropoff,
    DateTime SlaDueAt,
    DateTime? CompletedAt)
{
    public static AssignmentDto From(Assignment a) => new(
        a.Id, a.ShipmentId, a.TrackingNumber, a.CourierId, a.CourierName, a.Mode, a.AssignedBy, a.AssignedAt, a.RouteDate,
        a.Status, a.ShipmentStatus, a.ServiceLevel, a.WeightKg, a.DistanceKm, a.RecipientName,
        StopDto.From(a.Pickup), StopDto.From(a.Dropoff), a.SlaDueAt, a.CompletedAt);
}

public sealed record RouteDto(Guid CourierId, string CourierName, string Date, IReadOnlyList<AssignmentDto> Stops, int Open, int Done);

public sealed record DispatchSummary(int Couriers, int Available, int Idle, int Busy, int Full, int OffDuty, int Pending, int Offered, int Escalated, int ActiveAssignments);

public sealed class CourierRequestValidator : AbstractValidator<CourierRequest>
{
    public CourierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(40);
        RuleFor(x => x.VehicleType).IsInEnum();
        RuleFor(x => x.CapacityKg).GreaterThan(0).LessThanOrEqualTo(2000);
        RuleFor(x => x.MaxActiveShipments).InclusiveBetween(1, 50);
        RuleFor(x => x.HomeBase).NotNull();
        RuleFor(x => x.HomeBase.Lat).InclusiveBetween(-90, 90).When(x => x.HomeBase is not null);
        RuleFor(x => x.HomeBase.Lng).InclusiveBetween(-180, 180).When(x => x.HomeBase is not null);
    }
}

public sealed class AssignRequestValidator : AbstractValidator<AssignRequest>
{
    public AssignRequestValidator()
    {
        RuleFor(x => x.ShipmentId).NotEmpty();
        RuleFor(x => x.CourierId).NotEmpty();
    }
}
