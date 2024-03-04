using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using ParcelTrace.Protos.Shipments;
using ParcelTrace.Shipments.Data;
using ParcelTrace.Shipments.Domain;

namespace ParcelTrace.Shipments.Grpc;

public sealed class ShipmentLookupService(ShipmentsDbContext db) : ShipmentLookup.ShipmentLookupBase
{
    private const int MaxBatch = 100;

    public override async Task<ShipmentDetails> GetShipment(GetShipmentRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.ShipmentId, out var id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "shipment_id must be a GUID"));
        }

        var shipment = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, context.CancellationToken)
                       ?? throw new RpcException(new Status(StatusCode.NotFound, $"Shipment {id} not found"));
        return ToDetails(shipment);
    }

    public override async Task<GetShipmentsResponse> GetShipments(GetShipmentsRequest request, ServerCallContext context)
    {
        var ids = request.ShipmentIds.Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().Take(MaxBatch).ToList();
        var shipments = await db.Shipments.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(context.CancellationToken);
        var response = new GetShipmentsResponse();
        response.Shipments.AddRange(shipments.Select(ToDetails));
        return response;
    }

    internal static ShipmentDetails ToDetails(Shipment s) => new()
    {
        ShipmentId = s.Id.ToString(),
        TrackingNumber = s.TrackingNumber,
        Status = s.Status.ToString(),
        ServiceLevel = s.ServiceLevel.ToString(),
        WeightKg = (double)s.Package.WeightKg,
        Pickup = ToLocation(s.Pickup),
        Dropoff = ToLocation(s.Dropoff),
        RecipientName = s.Recipient.Name,
        CourierId = s.CourierId?.ToString() ?? string.Empty,
        SlaDueAt = Timestamp.FromDateTimeOffset(s.SlaDueAt),
        CreatedAt = Timestamp.FromDateTimeOffset(s.CreatedAt)
    };

    private static Location ToLocation(Address a) => new()
    {
        Line1 = a.Line1,
        City = a.City,
        PostalCode = a.PostalCode,
        Lat = a.Lat,
        Lng = a.Lng
    };
}
