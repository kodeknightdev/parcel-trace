using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ParcelTrace.Contracts;

namespace ParcelTrace.Frontend.Services;

public static class CookieClaims
{
    public const string AccessToken = "access_token";
}

public interface ICookieSignIn
{
    Task SignInAsync(HttpContext http, AuthResponse auth);
}

public sealed class CookieSignIn : ICookieSignIn
{
    public static ClaimsPrincipal CreatePrincipal(AuthResponse auth)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, auth.User.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, auth.User.Email),
            new(ParcelClaims.Name, auth.User.DisplayName),
            new(ParcelClaims.Role, auth.User.Role),
            new(CookieClaims.AccessToken, auth.AccessToken)
        };
        if (jwt.Claims.FirstOrDefault(c => c.Type == ParcelClaims.CourierId) is { } courier)
        {
            claims.Add(new Claim(ParcelClaims.CourierId, courier.Value));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, ParcelClaims.Name, ParcelClaims.Role));
    }

    public Task SignInAsync(HttpContext http, AuthResponse auth) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, CreatePrincipal(auth), new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = auth.ExpiresAt,
            AllowRefresh = false
        });
}
