using ParcelTrace.Contracts;

namespace ParcelTrace.Shipments.Domain;

public enum TransitionOutcome
{
    Apply,
    Duplicate,
    Stale,
    Terminal,
    Invalid
}

public static class ShipmentStateMachine
{
    private static readonly Dictionary<ShipmentStatus, ShipmentStatus[]> Allowed = new()
    {
        [ShipmentStatus.Created] = [ShipmentStatus.Assigned, ShipmentStatus.Cancelled],
        [ShipmentStatus.Assigned] = [ShipmentStatus.PickedUp, ShipmentStatus.Cancelled, ShipmentStatus.Failed],
        [ShipmentStatus.PickedUp] = [ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery, ShipmentStatus.Failed],
        [ShipmentStatus.InTransit] = [ShipmentStatus.OutForDelivery, ShipmentStatus.Failed],
        [ShipmentStatus.OutForDelivery] = [ShipmentStatus.Delivered, ShipmentStatus.Failed],
        [ShipmentStatus.Delivered] = [],
        [ShipmentStatus.Failed] = [],
        [ShipmentStatus.Cancelled] = []
    };

    public static bool IsTerminal(ShipmentStatus status) =>
        status is ShipmentStatus.Delivered or ShipmentStatus.Failed or ShipmentStatus.Cancelled;

    public static bool IsActive(ShipmentStatus status) =>
        status is ShipmentStatus.Assigned or ShipmentStatus.PickedUp or ShipmentStatus.InTransit or ShipmentStatus.OutForDelivery;

    public static bool CanTransition(ShipmentStatus from, ShipmentStatus to) => Allowed[from].Contains(to);

    public static IReadOnlyList<ShipmentStatus> NextStatuses(ShipmentStatus from) => Allowed[from];

    public static TransitionOutcome Evaluate(ShipmentStatus from, ShipmentStatus to)
    {
        if (from == to)
        {
            return TransitionOutcome.Duplicate;
        }

        if (IsTerminal(from))
        {
            return TransitionOutcome.Terminal;
        }

        if (CanTransition(from, to))
        {
            return TransitionOutcome.Apply;
        }

        return Rank(to) < Rank(from) ? TransitionOutcome.Stale : TransitionOutcome.Invalid;
    }

    private static int Rank(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Created => 0,
        ShipmentStatus.Assigned => 1,
        ShipmentStatus.PickedUp => 2,
        ShipmentStatus.InTransit => 3,
        ShipmentStatus.OutForDelivery => 4,
        _ => 5
    };
}
