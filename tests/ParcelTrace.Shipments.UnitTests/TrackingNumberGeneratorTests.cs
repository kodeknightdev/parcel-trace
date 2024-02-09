using Microsoft.Extensions.Time.Testing;
using ParcelTrace.Shipments.Domain;

namespace ParcelTrace.Shipments.UnitTests;

public class TrackingNumberGeneratorTests
{
    private readonly TrackingNumberGenerator _generator = new(new FakeTimeProvider(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Has_the_PT_year_suffix_format()
    {
        var tn = _generator.Next();

        tn.Should().MatchRegex("^PT-2024-[A-Z2-9]{6}$");
        TrackingNumberGenerator.IsValid(tn).Should().BeTrue();
    }

    [Fact]
    public void Never_uses_look_alike_characters()
    {
        var all = string.Concat(Enumerable.Range(0, 500).Select(_ => _generator.Next()[8..]));

        all.Should().NotContainAny("0", "O", "1", "I", "L");
    }

    [Fact]
    public void Is_practically_unique()
    {
        var numbers = Enumerable.Range(0, 5000).Select(_ => _generator.Next()).ToList();

        numbers.Distinct().Should().HaveCount(numbers.Count);
    }

    [Theory]
    [InlineData("PT-2024-ABC23")]
    [InlineData("PX-2024-ABC234")]
    [InlineData("PT-2024-ABC23O")]
    [InlineData("pt-2024-abc234")]
    [InlineData(null)]
    public void Rejects_malformed_numbers(string? value) => TrackingNumberGenerator.IsValid(value).Should().BeFalse();
}
