using System.Net.Http.Headers;

namespace ParcelTrace.Frontend.Services;

public sealed class AccessTokenHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = accessor.HttpContext?.User.FindFirst(CookieClaims.AccessToken)?.Value;
        if (!string.IsNullOrEmpty(token) && request.Headers.Authorization is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
