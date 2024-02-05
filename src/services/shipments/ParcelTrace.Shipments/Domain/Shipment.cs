using ParcelTrace.Contracts;

namespace ParcelTrace.Shipments.Domain;

public sealed class Party
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public sealed class Address
{
    public string Line1 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "DE";
    public double Lat { get; set; }
    public double Lng { get; set; }

    public GeoPoint Point => new(Lat, Lng);
    public AddressSummary Summary => new(City, PostalCode, Lat, Lng);
}

public sealed class PackageInfo
{
    public decimal WeightKg { get; set; }
    public int LengthCm { get; set; }
    public int WidthCm { get; set; }
    public int HeightCm { get; set; }
}

public sealed class DeliveryProof
{
    public string RecipientName { get; set; } = string.Empty;
    public string SignatureName { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public DateTimeOffset DeliveredAt { get; set; }
}

public sealed class ShipmentRuleException(string message) : Exception(message);

public sealed class Shipment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TrackingNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;

    public Party Sender { get; set; } = new();
    public Party Recipient { get; set; } = new();
    public Address Pickup { get; set; } = new();
    public Address Dropoff { get; set; } = new();
    public PackageInfo Package { get; set; } = new();
    public string? Notes { get; set; }

    public ServiceLevel ServiceLevel { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EUR";
    public double DistanceKm { get; set; }

    public ShipmentStatus Status { get; private set; } = ShipmentStatus.Created;
    public Guid? CourierId { get; private set; }
    public string? CourierName { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset CancelDeadline { get; set; }
    public DateTimeOffset SlaDueAt { get; set; }
    public DateTimeOffset? EtaAt { get; set; }
    public DateTimeOffset? EtaCalculatedAt { get; set; }
    public DateTimeOffset? AssignedAt { get; private set; }
    public DateTimeOffset? PickedUpAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public string? CancelReason { get; private set; }
    public string? FailureReason { get; private set; }
    public DeliveryProof? Proof { get; private set; }

    // mapped to xmin
    public uint Version { get; set; }

    public bool CanCancel(DateTimeOffset now) =>
        Status is ShipmentStatus.Created or ShipmentStatus.Assigned && now <= CancelDeadline;

    public bool IsDelayed(DateTimeOffset now) =>
        !ShipmentStateMachine.IsTerminal(Status) && (now > SlaDueAt || (EtaAt is { } eta && eta > SlaDueAt));

    public void Cancel(DateTimeOffset now, string reason)
    {
        if (Status == ShipmentStatus.Cancelled)
        {
            return;
        }

        if (!CanCancel(now))
        {
            throw new ShipmentRuleException(now > CancelDeadline
                ? "The cancellation window for this shipment has closed."
                : $"A shipment that is {Status} can no longer be cancelled.");
        }

        Status = ShipmentStatus.Cancelled;
        CancelledAt = now;
        CancelReason = reason;
        UpdatedAt = now;
    }

    public bool Assign(Guid courierId, string courierName, DateTimeOffset at)
    {
        if (Status == ShipmentStatus.Assigned)
        {
            if (CourierId == courierId)
            {
                return false;
            }
        }
        else
        {
            var outcome = ShipmentStateMachine.Evaluate(Status, ShipmentStatus.Assigned);
            if (outcome == TransitionOutcome.Invalid)
            {
                throw new ShipmentRuleException($"Cannot assign a courier to a {Status} shipment.");
            }

            if (outcome != TransitionOutcome.Apply)
            {
                return false;
            }
        }

        Status = ShipmentStatus.Assigned;
        CourierId = courierId;
        CourierName = courierName;
        AssignedAt = at;
        UpdatedAt = at;
        return true;
    }

    public bool ApplyStatus(ShipmentStatus next, DateTimeOffset at, string? note = null, DeliveryProof? proof = null)
    {
        if (next is ShipmentStatus.Assigned or ShipmentStatus.Cancelled or ShipmentStatus.Created)
        {
            throw new ShipmentRuleException($"{next} is not a courier status update.");
        }

        var outcome = ShipmentStateMachine.Evaluate(Status, next);
        switch (outcome)
        {
            case TransitionOutcome.Invalid:
                throw new ShipmentRuleException($"Cannot move a shipment from {Status} to {next}.");
            case not TransitionOutcome.Apply:
                return false;
        }

        if (next == ShipmentStatus.Delivered && proof is null)
        {
            throw new ShipmentRuleException("A delivery needs a proof of delivery.");
        }

        Status = next;
        UpdatedAt = at;
        switch (next)
        {
            case ShipmentStatus.PickedUp:
                PickedUpAt = at;
                break;
            case ShipmentStatus.Delivered:
                DeliveredAt = proof!.DeliveredAt;
                Proof = proof;
                EtaAt = null;
                break;
            case ShipmentStatus.Failed:
                FailureReason = string.IsNullOrWhiteSpace(note) ? "Delivery failed" : note;
                EtaAt = null;
                break;
        }

        return true;
    }

    public bool UpdateEta(DateTimeOffset etaAt, DateTimeOffset calculatedAt)
    {
        if (ShipmentStateMachine.IsTerminal(Status) || (EtaCalculatedAt is { } last && last >= calculatedAt))
        {
            return false;
        }

        EtaAt = etaAt;
        EtaCalculatedAt = calculatedAt;
        return true;
    }
}
