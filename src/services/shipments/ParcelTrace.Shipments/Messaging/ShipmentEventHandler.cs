using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParcelTrace.Contracts;
using ParcelTrace.Shipments.Data;
using ParcelTrace.Shipments.Domain;

namespace ParcelTrace.Shipments.Messaging;

public sealed class ShipmentEventHandler(ShipmentsDbContext db, ILogger<ShipmentEventHandler> logger)
{
    public const string ConsumerName = "shipments";

    public Task HandleCourierAssignedAsync(Guid eventId, CourierAssigned e, CancellationToken ct) =>
        HandleAsync(eventId, e.ShipmentId, shipment => shipment.Assign(e.CourierId, e.CourierName, e.AssignedAt), ct);

    public Task HandleStatusChangedAsync(Guid eventId, StatusChanged e, CancellationToken ct) =>
        HandleAsync(eventId, e.ShipmentId, shipment =>
        {
            if (e.Status == ShipmentStatus.Assigned)
            {
                return false; // handled via courier-assigned
            }

            var proof = e.ProofOfDelivery is { } pod
                ? new DeliveryProof { RecipientName = pod.RecipientName, SignatureName = pod.SignatureName, PhotoUrl = pod.PhotoUrl, DeliveredAt = pod.DeliveredAt }
                : null;
            return shipment.ApplyStatus(e.Status, e.OccurredAt, e.Note, proof);
        }, ct);

    public Task HandleEtaRecalculatedAsync(Guid eventId, EtaRecalculated e, CancellationToken ct) =>
        HandleAsync(eventId, e.ShipmentId, shipment => shipment.UpdateEta(e.EtaAt, e.CalculatedAt), ct);

    private async Task HandleAsync(Guid eventId, Guid shipmentId, Func<Shipment, bool> apply, CancellationToken ct)
    {
        if (await db.ProcessedMessages.AnyAsync(m => m.EventId == eventId && m.Consumer == ConsumerName, ct))
        {
            logger.LogDebug("Event {EventId} already processed", eventId);
            return;
        }

        var shipment = await db.Shipments.SingleOrDefaultAsync(s => s.Id == shipmentId, ct);
        if (shipment is null)
        {
            // shouldn't happen, the shipment is saved before its first event
            logger.LogWarning("Event {EventId} refers to unknown shipment {ShipmentId}; ignoring", eventId, shipmentId);
            return;
        }

        // out-of-order events throw so they go through the retry topic
        var changed = apply(shipment);
        db.ProcessedMessages.Add(new ProcessedMessage { EventId = eventId, Consumer = ConsumerName });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogDebug("Event {EventId} was processed concurrently", eventId);
            return;
        }

        if (changed)
        {
            logger.LogInformation("Shipment {TrackingNumber} is now {Status}", shipment.TrackingNumber, shipment.Status);
        }
    }
}
