using Grpc.Core;
using ParcelTrace.Contracts;
using ParcelTrace.Protos.Shipments;

namespace ParcelTrace.Dispatch.Shipments;

public sealed record ShipmentLocation(string Line1, string City, string PostalCode, double Lat, double Lng);

public sealed record ShipmentInfo(
    Guid ShipmentId,
    string TrackingNumber,
    ShipmentStatus Status,
    ServiceLevel ServiceLevel,
    double WeightKg,
    ShipmentLocation Pickup,
    ShipmentLocation Dropoff,
    string RecipientName,
    Guid? CourierId,
    DateTime SlaDueAt);

public interface IShipmentDirectory
{
    Task<ShipmentInfo?> GetAsync(Guid shipmentId, CancellationToken ct);
}

public sealed class GrpcShipmentDirectory(ShipmentLookup.ShipmentLookupClient client) : IShipmentDirectory
{
    public async Task<ShipmentInfo?> GetAsync(Guid shipmentId, CancellationToken ct)
    {
        try
        {
            var details = await client.GetShipmentAsync(new GetShipmentRequest { ShipmentId = shipmentId.ToString() },
                deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: ct);
            return Map(details);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    internal static ShipmentInfo Map(ShipmentDetails d) => new(
        Guid.Parse(d.ShipmentId),
        d.TrackingNumber,
        Enum.Parse<ShipmentStatus>(d.Status),
        Enum.Parse<ServiceLevel>(d.ServiceLevel),
        d.WeightKg,
        new ShipmentLocation(d.Pickup.Line1, d.Pickup.City, d.Pickup.PostalCode, d.Pickup.Lat, d.Pickup.Lng),
        new ShipmentLocation(d.Dropoff.Line1, d.Dropoff.City, d.Dropoff.PostalCode, d.Dropoff.Lat, d.Dropoff.Lng),
        d.RecipientName,
        Guid.TryParse(d.CourierId, out var courierId) ? courierId : null,
        d.SlaDueAt.ToDateTime());
}
