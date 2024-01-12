namespace ParcelTrace.BuildingBlocks.Auth;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "parcel-trace-identity";
    public string Audience { get; set; } = "parcel-trace";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 120;
}
