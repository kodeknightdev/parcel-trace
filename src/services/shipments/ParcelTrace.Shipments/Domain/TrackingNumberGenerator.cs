using System.Security.Cryptography;

namespace ParcelTrace.Shipments.Domain;

public interface ITrackingNumberGenerator
{
    string Next();
}

public sealed class TrackingNumberGenerator(TimeProvider clock) : ITrackingNumberGenerator
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int SuffixLength = 6;

    public string Next()
    {
        Span<char> suffix = stackalloc char[SuffixLength];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"PT-{clock.GetUtcNow().Year}-{suffix}";
    }

    public static bool IsValid(string? trackingNumber) =>
        trackingNumber is { Length: 3 + 5 + SuffixLength } tn
        && tn.StartsWith("PT-", StringComparison.Ordinal)
        && int.TryParse(tn.AsSpan(3, 4), out _)
        && tn[7] == '-'
        && tn[8..].All(c => Alphabet.Contains(c));
}
