using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.Contracts;
using ParcelTrace.Identity.Data;

namespace ParcelTrace.Identity.Auth;

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

public sealed class TokenService(IOptions<JwtSettings> options, TimeProvider clock)
{
    private readonly JwtSettings _settings = options.Value;

    public IssuedToken Issue(User user)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(_settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ParcelClaims.Name, user.DisplayName),
            new(ParcelClaims.Role, user.Role)
        };
        if (user.CourierId is { } courierId)
        {
            claims.Add(new Claim(ParcelClaims.CourierId, courierId.ToString()));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey)), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
