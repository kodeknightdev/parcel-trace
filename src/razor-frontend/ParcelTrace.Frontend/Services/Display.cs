using ParcelTrace.Contracts;

namespace ParcelTrace.Frontend.Services;

public static class Display
{
    public static string StatusLabel(ShipmentStatus status) => status switch
    {
        ShipmentStatus.PickedUp => "Picked up",
        ShipmentStatus.InTransit => "In transit",
        ShipmentStatus.OutForDelivery => "Out for delivery",
        _ => status.ToString()
    };

    public static string StatusClass(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Created => "text-bg-secondary",
        ShipmentStatus.Assigned => "text-bg-info",
        ShipmentStatus.PickedUp or ShipmentStatus.InTransit => "text-bg-primary",
        ShipmentStatus.OutForDelivery => "text-bg-warning",
        ShipmentStatus.Delivered => "text-bg-success",
        ShipmentStatus.Failed => "text-bg-danger",
        _ => "text-bg-dark"
    };

    public static string AvailabilityClass(string availability) => availability switch
    {
        "Idle" => "text-bg-success",
        "Busy" => "text-bg-primary",
        "Full" => "text-bg-warning",
        _ => "text-bg-secondary"
    };

    public static string Local(DateTimeOffset? value) => value?.ToLocalTime().ToString("ddd dd MMM, HH:mm") ?? "–";

    public static string Local(DateTime? value) => value is { } v ? Local(new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc))) : "–";

    public static string Money(decimal amount, string currency) => $"{amount:0.00} {currency}";

    public static string Eta(EtaView eta) => eta.MinutesRemaining is { } minutes
        ? $"{Local(eta.EtaAt)} ({minutes} min)"
        : Local(eta.EtaAt);

    public static IReadOnlyList<ShipmentStatus> CourierNextSteps(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Assigned => [ShipmentStatus.PickedUp],
        ShipmentStatus.PickedUp => [ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery],
        ShipmentStatus.InTransit => [ShipmentStatus.OutForDelivery],
        ShipmentStatus.OutForDelivery => [ShipmentStatus.Delivered],
        _ => []
    };
}
