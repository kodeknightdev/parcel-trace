using ParcelTrace.Contracts;
using ParcelTrace.Shipments.Domain;

namespace ParcelTrace.Shipments.UnitTests;

public class QuoteCalculatorTests
{
    private static readonly GeoPoint Alexanderplatz = new(52.5219, 13.4132);
    private static readonly GeoPoint Kudamm = new(52.5030, 13.3320);

    private readonly QuoteCalculator _calculator = new(new PricingOptions(), new ShipmentOptions());

    [Fact]
    public void Standard_quote_adds_up_base_distance_weight_and_surcharge()
    {
        var quote = _calculator.Calculate(new QuoteInput(Alexanderplatz, Kudamm, 1.2m, 30, 20, 10, ServiceLevel.Standard));

        // 5.9 km x 1.25
        quote.DistanceKm.Should().BeApproximately(7.4, 0.1);
        quote.BillableWeightKg.Should().Be(1.5m);
        quote.BaseFee.Should().Be(4.90m);
        quote.WeightFee.Should().Be(0m, "the first 2 kg are included");
        quote.FuelSurcharge.Should().Be(Math.Round((quote.BaseFee + quote.DistanceFee) * 0.06m, 2, MidpointRounding.AwayFromZero));
        quote.Total.Should().Be(quote.BaseFee + quote.DistanceFee + quote.WeightFee + quote.FuelSurcharge);
        quote.Currency.Should().Be("EUR");
        quote.SlaHours.Should().Be(48);
    }

    [Fact]
    public void Express_costs_more_and_has_a_shorter_sla()
    {
        var standard = _calculator.Calculate(new QuoteInput(Alexanderplatz, Kudamm, 3m, 30, 20, 10, ServiceLevel.Standard));
        var express = _calculator.Calculate(new QuoteInput(Alexanderplatz, Kudamm, 3m, 30, 20, 10, ServiceLevel.Express));

        express.Total.Should().BeGreaterThan(standard.Total);
        express.SlaHours.Should().BeLessThan(standard.SlaHours);
    }

    [Fact]
    public void Bulky_light_parcels_are_charged_by_volumetric_weight()
    {
        // 60*50*40/5000 = 24 kg
        var quote = _calculator.Calculate(new QuoteInput(Alexanderplatz, Kudamm, 2m, 60, 50, 40, ServiceLevel.Standard));

        quote.BillableWeightKg.Should().Be(24m);
        quote.WeightFee.Should().Be((24m - 2m) * 0.60m);
    }

    [Theory]
    [InlineData(0.2, 0.5)]
    [InlineData(2.0, 2.0)]
    [InlineData(2.01, 2.5)]
    [InlineData(7.6, 8.0)]
    public void Billable_weight_rounds_up_to_the_next_half_kilo(decimal actual, decimal expected) =>
        _calculator.BillableWeight(actual, 10, 10, 10).Should().Be(expected);

    [Fact]
    public void Very_short_trips_pay_the_minimum()
    {
        var quote = _calculator.Calculate(new QuoteInput(Alexanderplatz, Alexanderplatz, 0.5m, 10, 10, 10, ServiceLevel.Express));

        quote.DistanceKm.Should().Be(0);
        quote.Total.Should().Be(new PricingOptions().ExpressMinimum);
    }

    [Fact]
    public void Pricing_comes_from_configuration()
    {
        var calculator = new QuoteCalculator(new PricingOptions { StandardBaseFee = 10m, StandardPerKm = 0m, FuelSurchargeRate = 0m, Currency = "GBP" }, new ShipmentOptions());

        var quote = calculator.Calculate(new QuoteInput(Alexanderplatz, Kudamm, 1m, 10, 10, 10, ServiceLevel.Standard));

        quote.Total.Should().Be(10m);
        quote.Currency.Should().Be("GBP");
    }
}
