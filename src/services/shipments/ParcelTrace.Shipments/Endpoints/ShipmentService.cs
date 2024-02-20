using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ParcelTrace.Contracts;
using ParcelTrace.Shipments.Data;
using ParcelTrace.Shipments.Domain;
using ParcelTrace.Shipments.Outbox;

namespace ParcelTrace.Shipments.Endpoints;

public sealed class ShipmentService(
    ShipmentsDbContext db,
    QuoteCalculator quotes,
    ITrackingNumberGenerator trackingNumbers,
    IOptions<ShipmentOptions> options,
    TimeProvider clock,
    ILogger<ShipmentService> logger)
{
    private const int MaxTrackingNumberAttempts = 5;

    public async Task<Shipment> CreateAsync(CreateShipmentRequest request, Guid customerId, string customerEmail, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var quote = quotes.Calculate(new QuoteInput(
            new GeoPoint(request.Pickup.Lat, request.Pickup.Lng),
            new GeoPoint(request.Dropoff.Lat, request.Dropoff.Lng),
            request.Package.WeightKg, request.Package.LengthCm, request.Package.WidthCm, request.Package.HeightCm,
            request.ServiceLevel));

        var shipment = new Shipment
        {
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            Sender = new Party { Name = request.Sender.Name.Trim(), Phone = request.Sender.Phone.Trim(), Email = request.Sender.Email },
            Recipient = new Party { Name = request.Recipient.Name.Trim(), Phone = request.Recipient.Phone.Trim(), Email = request.Recipient.Email },
            Pickup = ToAddress(request.Pickup),
            Dropoff = ToAddress(request.Dropoff),
            Package = new PackageInfo
            {
                WeightKg = request.Package.WeightKg,
                LengthCm = request.Package.LengthCm,
                WidthCm = request.Package.WidthCm,
                HeightCm = request.Package.HeightCm
            },
            Notes = request.Notes,
            ServiceLevel = request.ServiceLevel,
            Price = quote.Total,
            Currency = quote.Currency,
            DistanceKm = quote.DistanceKm,
            CreatedAt = now,
            CancelDeadline = now.AddMinutes(options.Value.CancelWindowMinutes),
            SlaDueAt = now.AddHours(quote.SlaHours)
        };

        for (var attempt = 1; ; attempt++)
        {
            shipment.TrackingNumber = trackingNumbers.Next();
            db.Shipments.Add(shipment);
            db.Enqueue(Topics.ShipmentCreated, shipment.Id.ToString(), EventTypes.ShipmentCreated, new ShipmentCreated(
                shipment.Id, shipment.TrackingNumber, shipment.CustomerId, shipment.ServiceLevel,
                shipment.Pickup.Summary, shipment.Dropoff.Summary, shipment.Package.WeightKg,
                shipment.SlaDueAt, shipment.EtaAt, shipment.CreatedAt));
            try
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Created shipment {TrackingNumber} ({ServiceLevel}, {Price} {Currency})",
                    shipment.TrackingNumber, shipment.ServiceLevel, shipment.Price, shipment.Currency);
                return shipment;
            }
            catch (DbUpdateException ex) when (attempt < MaxTrackingNumberAttempts && IsTrackingNumberClash(ex))
            {
                logger.LogWarning("Tracking number {TrackingNumber} already taken, generating another", shipment.TrackingNumber);
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<Shipment> CancelAsync(Shipment shipment, string? reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (shipment.Status == ShipmentStatus.Cancelled)
        {
            return shipment;
        }

        shipment.Cancel(now, string.IsNullOrWhiteSpace(reason) ? "Cancelled by customer" : reason.Trim());
        db.Enqueue(Topics.ShipmentCancelled, shipment.Id.ToString(), EventTypes.ShipmentCancelled,
            new ShipmentCancelled(shipment.Id, shipment.TrackingNumber, shipment.CourierId, shipment.CancelReason!, now));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Cancelled shipment {TrackingNumber}", shipment.TrackingNumber);
        return shipment;
    }

    private static Address ToAddress(AddressDto a) => new()
    {
        Line1 = a.Line1.Trim(),
        City = a.City.Trim(),
        PostalCode = a.PostalCode.Trim(),
        Country = a.Country.ToUpperInvariant(),
        Lat = a.Lat,
        Lng = a.Lng
    };

    private static bool IsTrackingNumberClash(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName?.Contains("TrackingNumber", StringComparison.OrdinalIgnoreCase) == true;
}
