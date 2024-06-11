using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages.Shipments;

public sealed class DetailsModel(IGatewayClient gateway) : PageModel
{
    public ShipmentDto? Shipment { get; private set; }
    public IReadOnlyList<TimelineEntry> Timeline { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var result = await gateway.GetShipmentAsync(id, ct);
        if (!result.Ok)
        {
            return result.NotFound ? NotFound() : StatusCode(result.StatusCode);
        }

        Shipment = result.Value;
        // timeline may not exist yet
        var tracking = await gateway.TrackAsync(Shipment!.TrackingNumber, ct);
        Timeline = tracking.Ok ? tracking.Value!.Timeline : [];
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id, string? reason, CancellationToken ct)
    {
        var result = await gateway.CancelShipmentAsync(id, reason, ct);
        if (result.Ok)
        {
            TempData["Flash"] = $"Shipment {result.Value!.TrackingNumber} was cancelled.";
        }
        else
        {
            TempData["FlashError"] = result.Error;
        }

        return RedirectToPage(new { id });
    }
}
