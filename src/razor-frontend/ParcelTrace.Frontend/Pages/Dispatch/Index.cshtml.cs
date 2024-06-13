using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages.Dispatch;

public sealed class IndexModel(IGatewayClient gateway) : PageModel
{
    public DailyCounts? Counts { get; private set; }

    public DispatchBoard? Board { get; private set; }
    public DispatchSummary? Summary { get; private set; }
    public IReadOnlyList<PendingShipment> Queue { get; private set; } = [];
    public IReadOnlyList<CourierDto> Couriers { get; private set; } = [];
    public IReadOnlyList<ShipmentDto> Delayed { get; private set; } = [];
    public List<string> Errors { get; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var counts = gateway.DailyCountsAsync(ct);
        var summary = gateway.DispatchSummaryAsync(ct);
        var queue = gateway.QueueAsync(ct);
        var couriers = gateway.CouriersAsync(ct);
        var delayed = gateway.DelayedAsync(ct);
        var board = gateway.BoardAsync(ct);
        await Task.WhenAll(counts, summary, queue, couriers, delayed, board);

        Board = Take(await board, "dispatch board");
        Counts = Take(await counts, "counts");
        Summary = Take(await summary, "courier summary");
        Queue = Take(await queue, "queue") ?? [];
        Couriers = Take(await couriers, "couriers") ?? [];
        Delayed = Take(await delayed, "delayed shipments") ?? [];
    }

    public async Task<IActionResult> OnPostAssignAsync(Guid shipmentId, Guid courierId, CancellationToken ct)
    {
        var result = await gateway.AssignAsync(shipmentId, courierId, ct);
        Flash(result.Ok, result.Ok ? $"{result.Value!.TrackingNumber} assigned to {result.Value.CourierName}." : result.Error);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAutoAssignAsync(Guid shipmentId, CancellationToken ct)
    {
        var result = await gateway.AutoAssignAsync(shipmentId, ct);
        var offered = result.Ok && result.Value!.Offered;
        Flash(offered, offered
            ? $"{result.Value!.TrackingNumber} offered to {result.Value.CourierName} ({result.Value.DistanceKm:0.0} km away), waiting for an answer."
            : result.Value?.Reason ?? result.Error);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAutoAssignAllAsync(CancellationToken ct)
    {
        var result = await gateway.AutoAssignAllAsync(ct);
        Flash(result.Ok, result.Ok
            ? $"Made offers for {result.Value!.Count(r => r.Offered)} of {result.Value!.Count} waiting shipments."
            : result.Error);
        return RedirectToPage();
    }

    private T? Take<T>(ApiResult<T> result, string what)
    {
        if (!result.Ok)
        {
            Errors.Add($"Couldn't load {what}: {result.Error}");
        }

        return result.Value;
    }

    public int OnTheRoad => Board is null
        ? Counts?.InProgress ?? 0
        : new[] { "Assigned", "PickedUp", "InTransit", "OutForDelivery" }.Sum(s => Board.ByStatus.GetValueOrDefault(s));

    private void Flash(bool ok, string? message) => TempData[ok ? "Flash" : "FlashError"] = message ?? "Something went wrong.";
}
