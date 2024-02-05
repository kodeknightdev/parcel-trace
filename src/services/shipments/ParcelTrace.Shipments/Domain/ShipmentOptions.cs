using ParcelTrace.Contracts;

namespace ParcelTrace.Shipments.Domain;

public sealed class ShipmentOptions
{
    public const string SectionName = "Shipments";

    public int CancelWindowMinutes { get; set; } = 30;
    public int StandardSlaHours { get; set; } = 48;
    public int ExpressSlaHours { get; set; } = 8;

    public int SlaHours(ServiceLevel level) => level == ServiceLevel.Express ? ExpressSlaHours : StandardSlaHours;
}
