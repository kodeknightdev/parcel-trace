using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages.Courier;

public sealed class IndexModel(IGatewayClient gateway, TimeProvider clock) : PageModel
{
    public CourierDto? Courier { get; private set; }
    public RouteDto? Route { get; private set; }
    public IReadOnlyList<CourierOffer> Offers { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var me = gateway.MyCourierAsync(ct);
        var route = gateway.MyRouteAsync(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), ct);
        var offers = gateway.MyOffersAsync(ct);
        await Task.WhenAll(me, route, offers);

        Courier = (await me).Value;
        Offers = (await offers).Value ?? [];
        var routeResult = await route;
        Route = routeResult.Value;
        Error = routeResult.Ok ? null : routeResult.Error;
    }

    public async Task<IActionResult> OnPostStatusAsync(Guid shipmentId, string status, CancellationToken ct)
    {
        var result = await gateway.UpdateStatusAsync(shipmentId, new StatusUpdate(status), ct);
        TempData[result.Ok ? "Flash" : "FlashError"] = result.Ok
            ? $"{result.Value!.TrackingNumber}: {Display.StatusLabel(result.Value.Status)}"
            : result.Error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAcceptAsync(Guid shipmentId, CancellationToken ct)
    {
        var result = await gateway.AcceptOfferAsync(shipmentId, ct);
        TempData[result.Ok ? "Flash" : "FlashError"] = result.Ok ? $"{result.Value!.TrackingNumber} is on your route." : result.Error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid shipmentId, string? reason, CancellationToken ct)
    {
        var result = await gateway.RejectOfferAsync(shipmentId, reason, ct);
        TempData[result.Ok ? "Flash" : "FlashError"] = result.Ok ? $"Offer for {result.Value!.TrackingNumber} declined." : result.Error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAvailabilityAsync(bool available, CancellationToken ct)
    {
        var result = await gateway.SetMyAvailabilityAsync(available, ct);
        TempData[result.Ok ? "Flash" : "FlashError"] = result.Ok ? (available ? "You're on duty." : "You're off duty.") : result.Error;
        return RedirectToPage();
    }
}
