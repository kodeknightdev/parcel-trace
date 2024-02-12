using FluentValidation;
using ParcelTrace.Contracts;
using ParcelTrace.Shipments.Domain;

namespace ParcelTrace.Shipments.Endpoints;

public sealed record PartyDto(string Name, string Phone, string? Email = null);

public sealed record AddressDto(string Line1, string City, string PostalCode, string Country, double Lat, double Lng);

public sealed record PackageDto(decimal WeightKg, int LengthCm, int WidthCm, int HeightCm);

public sealed record QuoteRequest(GeoPoint Pickup, GeoPoint Dropoff, PackageDto Package, ServiceLevel ServiceLevel);

public sealed record CreateShipmentRequest(
    PartyDto Sender,
    PartyDto Recipient,
    AddressDto Pickup,
    AddressDto Dropoff,
    PackageDto Package,
    ServiceLevel ServiceLevel,
    string? Notes);

public sealed record CancelShipmentRequest(string? Reason);

public sealed record ProofDto(string RecipientName, string SignatureName, string? PhotoUrl, DateTimeOffset DeliveredAt);

public sealed record ShipmentDto(
    Guid Id,
    string TrackingNumber,
    ShipmentStatus Status,
    ServiceLevel ServiceLevel,
    Guid CustomerId,
    PartyDto Sender,
    PartyDto Recipient,
    AddressDto Pickup,
    AddressDto Dropoff,
    PackageDto Package,
    string? Notes,
    decimal Price,
    string Currency,
    double DistanceKm,
    Guid? CourierId,
    string? CourierName,
    DateTimeOffset CreatedAt,
    DateTimeOffset CancelDeadline,
    bool CanCancel,
    DateTimeOffset SlaDueAt,
    DateTimeOffset? EtaAt,
    bool IsDelayed,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    string? FailureReason,
    ProofDto? Proof)
{
    public static ShipmentDto From(Shipment s, DateTimeOffset now) => new(
        s.Id, s.TrackingNumber, s.Status, s.ServiceLevel, s.CustomerId,
        new PartyDto(s.Sender.Name, s.Sender.Phone, s.Sender.Email),
        new PartyDto(s.Recipient.Name, s.Recipient.Phone, s.Recipient.Email),
        ToDto(s.Pickup), ToDto(s.Dropoff),
        new PackageDto(s.Package.WeightKg, s.Package.LengthCm, s.Package.WidthCm, s.Package.HeightCm),
        s.Notes, s.Price, s.Currency, s.DistanceKm, s.CourierId, s.CourierName,
        s.CreatedAt, s.CancelDeadline, s.CanCancel(now), s.SlaDueAt, s.EtaAt, s.IsDelayed(now),
        s.AssignedAt, s.PickedUpAt, s.DeliveredAt, s.CancelledAt, s.CancelReason, s.FailureReason,
        s.Proof is { } p ? new ProofDto(p.RecipientName, p.SignatureName, p.PhotoUrl, p.DeliveredAt) : null);

    private static AddressDto ToDto(Address a) => new(a.Line1, a.City, a.PostalCode, a.Country, a.Lat, a.Lng);
}

public sealed record PublicShipmentDto(
    string TrackingNumber,
    ShipmentStatus Status,
    ServiceLevel ServiceLevel,
    string PickupCity,
    string DropoffCity,
    DateTimeOffset CreatedAt,
    DateTimeOffset SlaDueAt,
    DateTimeOffset? EtaAt,
    DateTimeOffset? DeliveredAt,
    string? DeliveredTo);

public sealed record ShipmentPage(IReadOnlyList<ShipmentDto> Items, int Page, int PageSize, int Total);

public sealed record DailyCounts(DateOnly Date, int Created, int Delivered, int Failed, int Cancelled, int Unassigned, int InProgress, int Delayed);

public sealed class GeoPointValidator : AbstractValidator<GeoPoint>
{
    public GeoPointValidator()
    {
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180);
    }
}

public sealed class PackageDtoValidator : AbstractValidator<PackageDto>
{
    public const decimal MaxWeightKg = 30m;
    public const int MaxSideCm = 150;

    public PackageDtoValidator()
    {
        RuleFor(x => x.WeightKg).GreaterThan(0).LessThanOrEqualTo(MaxWeightKg);
        RuleFor(x => x.LengthCm).InclusiveBetween(1, MaxSideCm);
        RuleFor(x => x.WidthCm).InclusiveBetween(1, MaxSideCm);
        RuleFor(x => x.HeightCm).InclusiveBetween(1, MaxSideCm);
    }
}

public sealed class PartyDtoValidator : AbstractValidator<PartyDto>
{
    public PartyDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(40).Matches(@"^[0-9+()\-\s]{5,40}$").WithMessage("Phone may contain digits, spaces, +, - and brackets.");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
    }
}

public sealed class AddressDtoValidator : AbstractValidator<AddressDto>
{
    public AddressDtoValidator()
    {
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().Length(2);
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180);
    }
}

public sealed class QuoteRequestValidator : AbstractValidator<QuoteRequest>
{
    public QuoteRequestValidator()
    {
        RuleFor(x => x.Pickup).NotNull().SetValidator(new GeoPointValidator());
        RuleFor(x => x.Dropoff).NotNull().SetValidator(new GeoPointValidator());
        RuleFor(x => x.Package).NotNull().SetValidator(new PackageDtoValidator());
        RuleFor(x => x.ServiceLevel).IsInEnum();
    }
}

public sealed class CreateShipmentRequestValidator : AbstractValidator<CreateShipmentRequest>
{
    public const double MaxDistanceKm = 300;

    public CreateShipmentRequestValidator()
    {
        RuleFor(x => x.Sender).NotNull().SetValidator(new PartyDtoValidator());
        RuleFor(x => x.Recipient).NotNull().SetValidator(new PartyDtoValidator());
        RuleFor(x => x.Pickup).NotNull().SetValidator(new AddressDtoValidator());
        RuleFor(x => x.Dropoff).NotNull().SetValidator(new AddressDtoValidator());
        RuleFor(x => x.Package).NotNull().SetValidator(new PackageDtoValidator());
        RuleFor(x => x.ServiceLevel).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x)
            .Must(x => Geo.HaversineKm(x.Pickup.Lat, x.Pickup.Lng, x.Dropoff.Lat, x.Dropoff.Lng) <= MaxDistanceKm)
            .When(x => x.Pickup is not null && x.Dropoff is not null)
            .WithName("Dropoff")
            .WithMessage($"Pick-up and drop-off must be within {MaxDistanceKm} km of each other (same-region courier service).");
    }
}
