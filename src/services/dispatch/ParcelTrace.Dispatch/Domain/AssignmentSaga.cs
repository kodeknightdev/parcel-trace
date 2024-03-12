using MongoDB.Bson.Serialization.Attributes;

namespace ParcelTrace.Dispatch.Domain;

public enum SagaState
{
    WaitingForCourier,
    Offered,
    Accepted,
    Escalated,
    ManuallyAssigned,
    Cancelled
}

public enum OfferOutcome
{
    Pending,
    Accepted,
    Rejected,
    TimedOut,
    Withdrawn
}

public enum SagaStep
{
    None,
    OfferNextCourier,
    Escalate
}

public sealed class CourierOffer
{
    public Guid CourierId { get; set; }
    public string CourierName { get; set; } = string.Empty;
    public double DistanceKm { get; set; }
    public DateTime OfferedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public OfferOutcome Outcome { get; set; } = OfferOutcome.Pending;
    public DateTime? RespondedAt { get; set; }
    public string? Reason { get; set; }
}

public sealed class SagaRuleException(string message) : Exception(message);

// offers for one shipment until a courier accepts or a dispatcher steps in
public sealed class AssignmentSaga
{
    [BsonId]
    public Guid ShipmentId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public SagaState State { get; set; } = SagaState.WaitingForCourier;
    public List<CourierOffer> Offers { get; set; } = [];

    public int Failures { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public string? EscalationReason { get; set; }

    public int Version { get; set; }

    public static AssignmentSaga Start(Guid shipmentId, string trackingNumber, DateTime now) => new()
    {
        ShipmentId = shipmentId,
        TrackingNumber = trackingNumber,
        CreatedAt = now,
        UpdatedAt = now
    };

    [BsonIgnore]
    public CourierOffer? CurrentOffer => State == SagaState.Offered ? Offers.LastOrDefault(o => o.Outcome == OfferOutcome.Pending) : null;

    [BsonIgnore]
    public IReadOnlyCollection<Guid> TriedCourierIds => Offers.Select(o => o.CourierId).ToHashSet();

    [BsonIgnore]
    public bool IsOpen => State is SagaState.WaitingForCourier or SagaState.Offered or SagaState.Escalated;

    public CourierOffer Offer(Guid courierId, string courierName, double distanceKm, DateTime now, TimeSpan timeout)
    {
        if (State != SagaState.WaitingForCourier)
        {
            throw new SagaRuleException($"Can't offer a shipment whose assignment is {State}.");
        }

        var offer = new CourierOffer
        {
            CourierId = courierId,
            CourierName = courierName,
            DistanceKm = distanceKm,
            OfferedAt = now,
            ExpiresAt = now.Add(timeout)
        };
        Offers.Add(offer);
        State = SagaState.Offered;
        UpdatedAt = now;
        return offer;
    }

    public CourierOffer Accept(Guid courierId, DateTime now)
    {
        var offer = RequireOfferFor(courierId);
        if (now > offer.ExpiresAt)
        {
            throw new SagaRuleException("This offer has expired.");
        }

        offer.Outcome = OfferOutcome.Accepted;
        offer.RespondedAt = now;
        State = SagaState.Accepted;
        UpdatedAt = now;
        return offer;
    }

    public SagaStep Reject(Guid courierId, DateTime now, string? reason, int maxAttempts)
    {
        var offer = RequireOfferFor(courierId);
        offer.Outcome = OfferOutcome.Rejected;
        offer.RespondedAt = now;
        offer.Reason = string.IsNullOrWhiteSpace(reason) ? "Rejected by courier" : reason.Trim();
        return AfterFailure(now, maxAttempts);
    }

    public SagaStep Expire(DateTime now, int maxAttempts)
    {
        if (CurrentOffer is not { } offer || offer.ExpiresAt >= now)
        {
            return SagaStep.None;
        }

        offer.Outcome = OfferOutcome.TimedOut;
        offer.RespondedAt = now;
        offer.Reason = "No answer in time";
        return AfterFailure(now, maxAttempts);
    }

    public CourierOffer? Withdraw(DateTime now, string reason)
    {
        if (CurrentOffer is not { } offer)
        {
            return null;
        }

        offer.Outcome = OfferOutcome.Withdrawn;
        offer.RespondedAt = now;
        offer.Reason = reason;
        UpdatedAt = now;
        return offer;
    }

    public CourierOffer? Cancel(DateTime now)
    {
        var withdrawn = Withdraw(now, "Shipment cancelled");
        State = SagaState.Cancelled;
        UpdatedAt = now;
        return withdrawn;
    }

    public CourierOffer? AssignManually(DateTime now)
    {
        var withdrawn = Withdraw(now, "Assigned by a dispatcher");
        State = SagaState.ManuallyAssigned;
        UpdatedAt = now;
        return withdrawn;
    }

    private SagaStep AfterFailure(DateTime now, int maxAttempts)
    {
        Failures++;
        UpdatedAt = now;
        if (Failures >= maxAttempts)
        {
            State = SagaState.Escalated;
            EscalatedAt = now;
            EscalationReason = $"{Failures} couriers rejected or didn't answer";
            return SagaStep.Escalate;
        }

        State = SagaState.WaitingForCourier;
        return SagaStep.OfferNextCourier;
    }

    private CourierOffer RequireOfferFor(Guid courierId)
    {
        if (CurrentOffer is not { } offer || offer.CourierId != courierId)
        {
            throw new SagaRuleException("You don't have an open offer for this shipment.");
        }

        return offer;
    }
}
