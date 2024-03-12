namespace ParcelTrace.Dispatch.Services;

public sealed class DispatchOptions
{
    public const string SectionName = "Dispatch";

    public bool AutoAssignOnCreate { get; set; } = true;

    public int SweepIntervalSeconds { get; set; } = 10;

    public int OfferTimeoutSeconds { get; set; } = 120;

    public int MaxOfferAttempts { get; set; } = 3;
}
