using Microsoft.Extensions.Options;
using MongoDB.Driver;
using ParcelTrace.BuildingBlocks.Errors;
using ParcelTrace.BuildingBlocks.Kafka;
using ParcelTrace.Contracts;
using ParcelTrace.Dispatch.Data;
using ParcelTrace.Dispatch.Domain;
using ParcelTrace.Dispatch.Shipments;

namespace ParcelTrace.Dispatch.Services;

public sealed record OfferResult(
    Guid ShipmentId,
    string TrackingNumber,
    bool Offered,
    SagaState State,
    Guid? CourierId,
    string? CourierName,
    double? DistanceKm,
    DateTime? ExpiresAt,
    string? Reason);

public sealed class AssignmentService(
    DispatchDb db,
    IShipmentDirectory shipments,
    IEventPublisher publisher,
    IOptions<DispatchOptions> options,
    TimeProvider clock,
    ILogger<AssignmentService> logger)
{
    private const int MaxReservationAttempts = 3;

    private DispatchOptions Options => options.Value;
    private TimeSpan OfferTimeout => TimeSpan.FromSeconds(Options.OfferTimeoutSeconds);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task QueueAsync(ShipmentCreated created, CancellationToken ct)
    {
        var alreadyAssigned = await db.Assignments.Find(a => a.ShipmentId == created.ShipmentId && a.Status == AssignmentStatus.Active).AnyAsync(ct);
        if (alreadyAssigned)
        {
            return;
        }

        var pending = new PendingShipment
        {
            ShipmentId = created.ShipmentId,
            TrackingNumber = created.TrackingNumber,
            ServiceLevel = created.ServiceLevel,
            PickupCity = created.Pickup.City,
            PickupPostalCode = created.Pickup.PostalCode,
            DropoffCity = created.Dropoff.City,
            PickupLat = created.Pickup.Lat,
            PickupLng = created.Pickup.Lng,
            WeightKg = (double)created.WeightKg,
            SlaDueAt = created.SlaDueAt.UtcDateTime,
            QueuedAt = Now
        };
        await db.Pending.ReplaceOneAsync(p => p.ShipmentId == pending.ShipmentId, pending, new ReplaceOptions { IsUpsert = true }, ct);
        await EnsureSagaAsync(created.ShipmentId, created.TrackingNumber, ct);
        logger.LogInformation("Queued {TrackingNumber} for assignment", created.TrackingNumber);
    }

    public async Task<OfferResult> OfferAsync(Guid shipmentId, CancellationToken ct)
    {
        var shipment = await shipments.GetAsync(shipmentId, ct) ?? throw new NotFoundException($"Shipment {shipmentId} not found.");
        var saga = await EnsureSagaAsync(shipmentId, shipment.TrackingNumber, ct);

        if (shipment.Status != ShipmentStatus.Created)
        {
            // already assigned, moving or cancelled
            await CloseAsync(saga, shipment.Status == ShipmentStatus.Cancelled ? SagaState.Cancelled : SagaState.ManuallyAssigned, ct);
            return Result(saga, "The shipment no longer needs a courier.");
        }

        if (saga.State != SagaState.WaitingForCourier)
        {
            return Result(saga, saga.State == SagaState.Escalated ? saga.EscalationReason : null);
        }

        for (var attempt = 1; attempt <= MaxReservationAttempts; attempt++)
        {
            var candidates = await db.Couriers.Find(c => c.IsAvailable).ToListAsync(ct);
            var tried = saga.TriedCourierIds;
            var choice = AutoAssigner.PickBest(new GeoPoint(shipment.Pickup.Lat, shipment.Pickup.Lng), shipment.WeightKg,
                candidates.Where(c => !tried.Contains(c.Id)), Now);
            if (choice is null)
            {
                await RecordWaitingAsync(shipmentId, "No available courier", ct);
                return Result(saga, "No available courier can take this parcel right now.");
            }

            // hold capacity while the offer is open
            if (!await TryReserveAsync(choice.Courier, shipment.ShipmentId, shipment.WeightKg, ct))
            {
                continue;
            }

            var offer = saga.Offer(choice.Courier.Id, choice.Courier.Name, choice.DistanceKm, Now, OfferTimeout);
            if (!await SaveAsync(saga, ct))
            {
                // lost the race, give the hold back
                await ReleaseCourierAsync(choice.Courier.Id, shipmentId, shipment.WeightKg, ct);
                return Result(await LoadSagaAsync(shipmentId, ct) ?? saga, "The assignment changed concurrently.");
            }

            await db.Pending.UpdateOneAsync(p => p.ShipmentId == shipmentId,
                Builders<PendingShipment>.Update.Set(p => p.OfferedTo, offer.CourierName).Set(p => p.OfferExpiresAt, offer.ExpiresAt)
                    .Inc(p => p.Attempts, 1).Set(p => p.LastAttemptAt, Now).Set(p => p.LastError, null),
                cancellationToken: ct);
            logger.LogInformation("Offered {TrackingNumber} to {Courier} ({Distance} km away), expires {ExpiresAt:HH:mm:ss}",
                saga.TrackingNumber, offer.CourierName, offer.DistanceKm, offer.ExpiresAt);
            return Result(saga, null);
        }

        throw new ConflictException("Couriers are changing too quickly to make an offer right now, try again.");
    }

    public async Task<IReadOnlyList<OfferResult>> OfferPendingAsync(CancellationToken ct, int limit = 50)
    {
        // express first, escalated ones wait for a dispatcher
        var pending = await db.Pending.Find(p => !p.Escalated)
            .SortByDescending(p => p.ServiceLevel).ThenBy(p => p.QueuedAt)
            .Limit(limit).ToListAsync(ct);

        var results = new List<OfferResult>(pending.Count);
        foreach (var p in pending)
        {
            var saga = await LoadSagaAsync(p.ShipmentId, ct);
            if (saga is { State: not SagaState.WaitingForCourier })
            {
                continue;
            }

            try
            {
                results.Add(await OfferAsync(p.ShipmentId, ct));
            }
            catch (AppException ex)
            {
                results.Add(new OfferResult(p.ShipmentId, p.TrackingNumber, false, saga?.State ?? SagaState.WaitingForCourier, null, null, null, null, ex.Message));
            }
        }

        return results;
    }

    public async Task<Assignment> AcceptOfferAsync(Guid shipmentId, Guid courierId, CancellationToken ct)
    {
        var saga = await LoadSagaAsync(shipmentId, ct) ?? throw new NotFoundException("No offer for this shipment.");
        var offer = Apply(() => saga.Accept(courierId, Now));
        var shipment = await shipments.GetAsync(shipmentId, ct) ?? throw new NotFoundException($"Shipment {shipmentId} not found.");
        var courier = await db.Couriers.Find(c => c.Id == courierId).FirstAsync(ct);

        if (!await SaveAsync(saga, ct))
        {
            throw new ConflictException("The offer changed while you answered (it may have just expired).");
        }

        // capacity is already held from the offer
        var assignment = NewAssignment(shipment, courier, AssignmentMode.Auto, $"accepted by {courier.Name}", offer.DistanceKm);
        try
        {
            await db.Assignments.InsertOneAsync(assignment, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            await ReleaseCourierAsync(courierId, shipmentId, shipment.WeightKg, ct);
            throw new ConflictException($"Shipment {shipment.TrackingNumber} was assigned by someone else just now.");
        }

        await db.Pending.DeleteOneAsync(p => p.ShipmentId == shipmentId, ct);
        await PublishAssignedAsync(assignment, ct);
        logger.LogInformation("{Courier} accepted {TrackingNumber}", courier.Name, shipment.TrackingNumber);
        return assignment;
    }

    public async Task<OfferResult> RejectOfferAsync(Guid shipmentId, Guid courierId, string? reason, CancellationToken ct)
    {
        var saga = await LoadSagaAsync(shipmentId, ct) ?? throw new NotFoundException("No offer for this shipment.");
        var weight = await WeightOfAsync(shipmentId, ct);
        var step = Apply(() => saga.Reject(courierId, Now, reason, Options.MaxOfferAttempts));
        if (!await SaveAsync(saga, ct))
        {
            throw new ConflictException("The offer changed while you answered (it may have just expired).");
        }

        await ReleaseCourierAsync(courierId, shipmentId, weight, ct);
        logger.LogInformation("Courier {CourierId} rejected {TrackingNumber}: {Reason}", courierId, saga.TrackingNumber, reason);
        return await ContinueAsync(saga, step, ct);
    }

    public async Task<int> ExpireOffersAsync(CancellationToken ct)
    {
        var now = Now;
        var expired = await db.Sagas
            .Find(s => s.State == SagaState.Offered && s.Offers.Any(o => o.Outcome == OfferOutcome.Pending && o.ExpiresAt < now))
            .Limit(100).ToListAsync(ct);

        var count = 0;
        foreach (var saga in expired)
        {
            var courierId = saga.CurrentOffer!.CourierId;
            var step = saga.Expire(now, Options.MaxOfferAttempts);
            if (step == SagaStep.None || !await SaveAsync(saga, ct))
            {
                continue; // answered meanwhile
            }

            count++;
            await ReleaseCourierAsync(courierId, saga.ShipmentId, await WeightOfAsync(saga.ShipmentId, ct), ct);
            logger.LogInformation("Offer of {TrackingNumber} to courier {CourierId} timed out", saga.TrackingNumber, courierId);
            try
            {
                await ContinueAsync(saga, step, ct);
            }
            catch (AppException ex)
            {
                logger.LogWarning("Could not re-offer {TrackingNumber}: {Reason}", saga.TrackingNumber, ex.Message);
            }
        }

        return count;
    }

    public Task<List<AssignmentSaga>> OffersForCourierAsync(Guid courierId, CancellationToken ct) =>
        db.Sagas.Find(s => s.State == SagaState.Offered && s.Offers.Any(o => o.CourierId == courierId && o.Outcome == OfferOutcome.Pending))
            .SortBy(s => s.UpdatedAt).ToListAsync(ct);

    public async Task<Assignment> AssignManualAsync(Guid shipmentId, Guid courierId, string assignedBy, CancellationToken ct)
    {
        var shipment = await shipments.GetAsync(shipmentId, ct) ?? throw new NotFoundException($"Shipment {shipmentId} not found.");
        if (shipment.Status is not (ShipmentStatus.Created or ShipmentStatus.Assigned))
        {
            await db.Pending.DeleteOneAsync(p => p.ShipmentId == shipmentId, ct);
            throw new ConflictException($"Shipment {shipment.TrackingNumber} is {shipment.Status} and can't be (re)assigned.");
        }

        var current = await db.Assignments.Find(a => a.ShipmentId == shipmentId && a.Status == AssignmentStatus.Active).FirstOrDefaultAsync(ct);
        if (current?.CourierId == courierId)
        {
            return current;
        }

        for (var attempt = 1; attempt <= MaxReservationAttempts; attempt++)
        {
            var courier = await db.Couriers.Find(c => c.Id == courierId).FirstOrDefaultAsync(ct)
                          ?? throw new NotFoundException($"Courier {courierId} not found.");
            if (!AutoAssigner.CanCarry(courier, shipment.WeightKg) && !HoldsOfferFor(courier, shipmentId))
            {
                throw new ConflictException(!courier.IsAvailable
                    ? $"{courier.Name} is off duty."
                    : $"{courier.Name} has no room left ({courier.ActiveCount}/{courier.MaxActiveShipments} parcels, {courier.CurrentLoadKg:0.#}/{courier.CapacityKg:0.#} kg).");
            }

            // withdraw any open offer first
            await WithdrawOfferAsync(shipmentId, shipment.WeightKg, ct);
            courier = await db.Couriers.Find(c => c.Id == courierId).FirstAsync(ct);

            if (!await TryReserveAsync(courier, shipmentId, shipment.WeightKg, ct))
            {
                continue;
            }

            var distanceKm = Math.Round(Geo.HaversineKm(AutoAssigner.CurrentPosition(courier, Now), new GeoPoint(shipment.Pickup.Lat, shipment.Pickup.Lng)), 2);
            var assignment = NewAssignment(shipment, courier, AssignmentMode.Manual, assignedBy, distanceKm);
            try
            {
                if (current is not null)
                {
                    await ReleaseAsync(current, AssignmentStatus.Reassigned, ct);
                }

                await db.Assignments.InsertOneAsync(assignment, cancellationToken: ct);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                await ReleaseCourierAsync(courier.Id, shipmentId, shipment.WeightKg, ct);
                throw new ConflictException($"Shipment {shipment.TrackingNumber} was assigned by someone else just now.");
            }

            await db.Pending.DeleteOneAsync(p => p.ShipmentId == shipmentId, ct);
            await PublishAssignedAsync(assignment, ct);
            logger.LogInformation("{By} assigned {TrackingNumber} to {Courier}", assignedBy, shipment.TrackingNumber, courier.Name);
            return assignment;
        }

        throw new ConflictException("Couriers are changing too quickly to assign right now, try again.");
    }

    public async Task CompleteAsync(StatusChanged change, CancellationToken ct)
    {
        var assignment = await db.Assignments.Find(a => a.ShipmentId == change.ShipmentId && a.Status == AssignmentStatus.Active).FirstOrDefaultAsync(ct);
        if (assignment is null)
        {
            return;
        }

        if (change.Status is ShipmentStatus.Delivered or ShipmentStatus.Failed)
        {
            await ReleaseAsync(assignment, change.Status == ShipmentStatus.Delivered ? AssignmentStatus.Completed : AssignmentStatus.Failed, ct, change.Status);
            logger.LogInformation("{TrackingNumber} {Status}; {Courier} is free for more work", assignment.TrackingNumber, change.Status, assignment.CourierName);
        }
        else
        {
            await db.Assignments.UpdateOneAsync(a => a.Id == assignment.Id, Builders<Assignment>.Update.Set(a => a.ShipmentStatus, change.Status), cancellationToken: ct);
        }
    }

    public async Task CancelAsync(ShipmentCancelled cancelled, CancellationToken ct)
    {
        await db.Pending.DeleteOneAsync(p => p.ShipmentId == cancelled.ShipmentId, ct);

        if (await LoadSagaAsync(cancelled.ShipmentId, ct) is { IsOpen: true } saga)
        {
            var withdrawn = saga.Cancel(Now);
            if (await SaveAsync(saga, ct) && withdrawn is not null)
            {
                await ReleaseCourierAsync(withdrawn.CourierId, saga.ShipmentId, await WeightOfAsync(saga.ShipmentId, ct), ct);
            }
        }

        var assignment = await db.Assignments.Find(a => a.ShipmentId == cancelled.ShipmentId && a.Status == AssignmentStatus.Active).FirstOrDefaultAsync(ct);
        if (assignment is not null)
        {
            await ReleaseAsync(assignment, AssignmentStatus.Cancelled, ct, ShipmentStatus.Cancelled);
            logger.LogInformation("{TrackingNumber} cancelled, released {Courier}", assignment.TrackingNumber, assignment.CourierName);
        }
    }

    public Task UpdateCourierLocationAsync(LocationUpdated location, CancellationToken ct) =>
        db.Couriers.UpdateOneAsync(
            c => c.Id == location.CourierId && (c.LastSeenAt == null || c.LastSeenAt < location.RecordedAt.UtcDateTime),
            Builders<Courier>.Update
                .Set(c => c.LastLocation, Position.From(location.Lat, location.Lng))
                .Set(c => c.LastSeenAt, location.RecordedAt.UtcDateTime),
            cancellationToken: ct);

    public async Task<IReadOnlyList<Assignment>> RouteAsync(Guid courierId, DateOnly date, CancellationToken ct)
    {
        var day = date.ToString("yyyy-MM-dd");
        // today's stops + anything still open
        var assignments = await db.Assignments
            .Find(a => a.CourierId == courierId && (a.RouteDate == day || a.Status == AssignmentStatus.Active))
            .SortBy(a => a.AssignedAt)
            .ToListAsync(ct);
        return assignments.Where(a => a.Status != AssignmentStatus.Reassigned).ToList();
    }

    public Task<AssignmentSaga?> LoadSagaAsync(Guid shipmentId, CancellationToken ct) =>
        db.Sagas.Find(s => s.ShipmentId == shipmentId).FirstOrDefaultAsync(ct)!;

    private async Task<OfferResult> ContinueAsync(AssignmentSaga saga, SagaStep step, CancellationToken ct)
    {
        switch (step)
        {
            case SagaStep.OfferNextCourier:
                await db.Pending.UpdateOneAsync(p => p.ShipmentId == saga.ShipmentId,
                    Builders<PendingShipment>.Update.Set(p => p.OfferedTo, null).Set(p => p.OfferExpiresAt, null), cancellationToken: ct);
                return await OfferAsync(saga.ShipmentId, ct);
            case SagaStep.Escalate:
                await db.Pending.UpdateOneAsync(p => p.ShipmentId == saga.ShipmentId,
                    Builders<PendingShipment>.Update.Set(p => p.Escalated, true).Set(p => p.OfferedTo, null).Set(p => p.OfferExpiresAt, null)
                        .Set(p => p.LastError, saga.EscalationReason),
                    cancellationToken: ct);
                logger.LogWarning("Escalated {TrackingNumber} to dispatchers: {Reason}", saga.TrackingNumber, saga.EscalationReason);
                return Result(saga, saga.EscalationReason);
            default:
                return Result(saga, null);
        }
    }

    private async Task<AssignmentSaga> EnsureSagaAsync(Guid shipmentId, string trackingNumber, CancellationToken ct)
    {
        if (await LoadSagaAsync(shipmentId, ct) is { } existing)
        {
            return existing;
        }

        var saga = AssignmentSaga.Start(shipmentId, trackingNumber, Now);
        try
        {
            await db.Sagas.InsertOneAsync(saga, cancellationToken: ct);
            return saga;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return (await LoadSagaAsync(shipmentId, ct))!; // created concurrently
        }
    }

    private async Task<bool> SaveAsync(AssignmentSaga saga, CancellationToken ct)
    {
        var expected = saga.Version;
        saga.Version = expected + 1;
        var result = await db.Sagas.ReplaceOneAsync(s => s.ShipmentId == saga.ShipmentId && s.Version == expected, saga, cancellationToken: ct);
        if (result.ModifiedCount == 1)
        {
            return true;
        }

        saga.Version = expected;
        return false;
    }

    private async Task CloseAsync(AssignmentSaga saga, SagaState state, CancellationToken ct)
    {
        if (!saga.IsOpen)
        {
            return;
        }

        var withdrawn = state == SagaState.Cancelled ? saga.Cancel(Now) : saga.AssignManually(Now);
        if (await SaveAsync(saga, ct) && withdrawn is not null)
        {
            await ReleaseCourierAsync(withdrawn.CourierId, saga.ShipmentId, await WeightOfAsync(saga.ShipmentId, ct), ct);
        }

        await db.Pending.DeleteOneAsync(p => p.ShipmentId == saga.ShipmentId, ct);
    }

    private async Task WithdrawOfferAsync(Guid shipmentId, double weightKg, CancellationToken ct)
    {
        if (await LoadSagaAsync(shipmentId, ct) is not { IsOpen: true } saga)
        {
            return;
        }

        var withdrawn = saga.AssignManually(Now);
        if (await SaveAsync(saga, ct) && withdrawn is not null)
        {
            await ReleaseCourierAsync(withdrawn.CourierId, shipmentId, weightKg, ct);
        }
    }

    private static bool HoldsOfferFor(Courier courier, Guid shipmentId) => courier.IsAvailable && courier.ActiveShipmentIds.Contains(shipmentId);

    private async Task<double> WeightOfAsync(Guid shipmentId, CancellationToken ct) =>
        await db.Pending.Find(p => p.ShipmentId == shipmentId).Project(p => (double?)p.WeightKg).FirstOrDefaultAsync(ct)
        ?? (await shipments.GetAsync(shipmentId, ct))?.WeightKg ?? 0;

    private Task RecordWaitingAsync(Guid shipmentId, string reason, CancellationToken ct) =>
        db.Pending.UpdateOneAsync(p => p.ShipmentId == shipmentId,
            Builders<PendingShipment>.Update.Inc(p => p.Attempts, 1).Set(p => p.LastAttemptAt, Now).Set(p => p.LastError, reason),
            cancellationToken: ct);

    private Task PublishAssignedAsync(Assignment a, CancellationToken ct) =>
        publisher.PublishAsync(Topics.CourierAssigned, a.ShipmentId.ToString(), EventTypes.CourierAssigned,
            new CourierAssigned(a.ShipmentId, a.TrackingNumber, a.CourierId, a.CourierName, a.Mode, clock.GetUtcNow()), ct);

    private async Task<bool> TryReserveAsync(Courier courier, Guid shipmentId, double weightKg, CancellationToken ct)
    {
        // only if the load hasn't changed since we read it
        var result = await db.Couriers.UpdateOneAsync(
            c => c.Id == courier.Id && c.IsAvailable && c.ActiveCount == courier.ActiveCount && !c.ActiveShipmentIds.Contains(shipmentId),
            Builders<Courier>.Update
                .Push(c => c.ActiveShipmentIds, shipmentId)
                .Inc(c => c.ActiveCount, 1)
                .Inc(c => c.CurrentLoadKg, weightKg),
            cancellationToken: ct);
        return result.ModifiedCount == 1;
    }

    private async Task ReleaseAsync(Assignment assignment, AssignmentStatus status, CancellationToken ct, ShipmentStatus? shipmentStatus = null)
    {
        var update = Builders<Assignment>.Update.Set(a => a.Status, status).Set(a => a.CompletedAt, Now);
        if (shipmentStatus is { } s)
        {
            update = update.Set(a => a.ShipmentStatus, s);
        }

        var closed = await db.Assignments.UpdateOneAsync(a => a.Id == assignment.Id && a.Status == AssignmentStatus.Active, update, cancellationToken: ct);
        if (closed.ModifiedCount == 1)
        {
            await ReleaseCourierAsync(assignment.CourierId, assignment.ShipmentId, assignment.WeightKg, ct);
        }
    }

    private Task ReleaseCourierAsync(Guid courierId, Guid shipmentId, double weightKg, CancellationToken ct) =>
        db.Couriers.UpdateOneAsync(
            c => c.Id == courierId && c.ActiveShipmentIds.Contains(shipmentId),
            Builders<Courier>.Update
                .Pull(c => c.ActiveShipmentIds, shipmentId)
                .Inc(c => c.ActiveCount, -1)
                .Inc(c => c.CurrentLoadKg, -weightKg),
            cancellationToken: ct);

    private static T Apply<T>(Func<T> rule)
    {
        try
        {
            return rule();
        }
        catch (SagaRuleException ex)
        {
            throw new ConflictException(ex.Message);
        }
    }

    private static OfferResult Result(AssignmentSaga saga, string? reason)
    {
        var offer = saga.CurrentOffer;
        return new OfferResult(saga.ShipmentId, saga.TrackingNumber, offer is not null, saga.State,
            offer?.CourierId, offer?.CourierName, offer?.DistanceKm, offer?.ExpiresAt, reason);
    }

    private Assignment NewAssignment(ShipmentInfo shipment, Courier courier, AssignmentMode mode, string assignedBy, double distanceKm) => new()
    {
        ShipmentId = shipment.ShipmentId,
        TrackingNumber = shipment.TrackingNumber,
        CourierId = courier.Id,
        CourierName = courier.Name,
        Mode = mode,
        AssignedBy = assignedBy,
        AssignedAt = Now,
        RouteDate = Now.ToString("yyyy-MM-dd"),
        ServiceLevel = shipment.ServiceLevel,
        WeightKg = shipment.WeightKg,
        DistanceKm = distanceKm,
        RecipientName = shipment.RecipientName,
        Pickup = ToStop(shipment.Pickup),
        Dropoff = ToStop(shipment.Dropoff),
        SlaDueAt = shipment.SlaDueAt
    };

    private static Stop ToStop(ShipmentLocation l) => new() { Line1 = l.Line1, City = l.City, PostalCode = l.PostalCode, Lat = l.Lat, Lng = l.Lng };
}
