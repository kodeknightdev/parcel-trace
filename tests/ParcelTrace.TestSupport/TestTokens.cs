using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ParcelTrace.Contracts;

namespace ParcelTrace.TestSupport;

public static class TestTokens
{
    public const string SigningKey = "integration-test-signing-key-0123456789abcdef";
    public const string Issuer = "parcel-trace-identity";
    public const string Audience = "parcel-trace";

    public static string For(Guid userId, string role, string email = "test@parceltrace.local", Guid? courierId = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ParcelClaims.Name, "Test User"),
            new(ParcelClaims.Role, role)
        };
        if (courierId is { } id)
        {
            claims.Add(new Claim(ParcelClaims.CourierId, id.ToString()));
        }

        var token = new JwtSecurityToken(Issuer, Audience, claims, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(30),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
