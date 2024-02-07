using ParcelTrace.Contracts;

namespace ParcelTrace.Shipments.Domain;

public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    public string Currency { get; set; } = "EUR";
    public decimal StandardBaseFee { get; set; } = 4.90m;
    public decimal ExpressBaseFee { get; set; } = 9.90m;
    public decimal StandardPerKm { get; set; } = 0.45m;
    public decimal ExpressPerKm { get; set; } = 0.75m;
    public decimal IncludedWeightKg { get; set; } = 2m;
    public decimal StandardPerKg { get; set; } = 0.60m;
    public decimal ExpressPerKg { get; set; } = 0.90m;
    public int VolumetricDivisor { get; set; } = 5000;
    public decimal FuelSurchargeRate { get; set; } = 0.06m;
    public decimal StandardMinimum { get; set; } = 5.90m;
    public decimal ExpressMinimum { get; set; } = 11.90m;
    public double RoadFactor { get; set; } = 1.25;
}

public sealed record QuoteInput(GeoPoint Pickup, GeoPoint Dropoff, decimal WeightKg, int LengthCm, int WidthCm, int HeightCm, ServiceLevel ServiceLevel);

public sealed record Quote(
    ServiceLevel ServiceLevel,
    double DistanceKm,
    decimal BillableWeightKg,
    decimal BaseFee,
    decimal DistanceFee,
    decimal WeightFee,
    decimal FuelSurcharge,
    decimal Total,
    string Currency,
    int SlaHours);

public sealed class QuoteCalculator(PricingOptions pricing, ShipmentOptions shipmentOptions)
{
    public Quote Calculate(QuoteInput input)
    {
        var express = input.ServiceLevel == ServiceLevel.Express;
        var distanceKm = Math.Round(Geo.HaversineKm(input.Pickup, input.Dropoff) * pricing.RoadFactor, 1);
        var billable = BillableWeight(input.WeightKg, input.LengthCm, input.WidthCm, input.HeightCm);

        var baseFee = express ? pricing.ExpressBaseFee : pricing.StandardBaseFee;
        var distanceFee = Money((decimal)distanceKm * (express ? pricing.ExpressPerKm : pricing.StandardPerKm));
        var weightFee = Money(Math.Max(0, billable - pricing.IncludedWeightKg) * (express ? pricing.ExpressPerKg : pricing.StandardPerKg));
        var subtotal = baseFee + distanceFee + weightFee;
        var surcharge = Money(subtotal * pricing.FuelSurchargeRate);
        var total = Math.Max(Money(subtotal + surcharge), express ? pricing.ExpressMinimum : pricing.StandardMinimum);

        return new Quote(input.ServiceLevel, distanceKm, billable, baseFee, distanceFee, weightFee, surcharge, total,
            pricing.Currency, shipmentOptions.SlaHours(input.ServiceLevel));
    }

    public decimal BillableWeight(decimal weightKg, int lengthCm, int widthCm, int heightCm)
    {
        var volumetric = (decimal)lengthCm * widthCm * heightCm / pricing.VolumetricDivisor;
        var weight = Math.Max(weightKg, volumetric);
        return Math.Ceiling(weight * 2) / 2;
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
