using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages;

public sealed class TrackModel(IGatewayClient gateway) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Tn { get; set; }

    public PublicTracking? Tracking { get; private set; }
    public string? Error { get; private set; }

    public IReadOnlyList<string> Missing => Tracking is null
        ? []
        : new[] { ("bookings", Tracking.Sources.Shipments), ("timeline", Tracking.Sources.Tracking), ("live ETA", Tracking.Sources.Eta) }
            .Where(s => s.Item2 == "unavailable").Select(s => s.Item1).ToList();

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Tn))
        {
            return;
        }

        var result = await gateway.TrackAsync(Tn, ct);
        if (result.Ok)
        {
            Tracking = result.Value;
        }
        else
        {
            Error = result.NotFound ? $"We couldn't find a parcel with tracking number {Tn.Trim().ToUpperInvariant()}." : result.Error;
        }
    }

    public async Task<IActionResult> OnGetDataAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Tn))
        {
            return BadRequest();
        }

        var result = await gateway.TrackAsync(Tn, ct);
        return result.Ok ? new JsonResult(result.Value, GatewayClient.Json) : StatusCode(result.StatusCode);
    }
}
