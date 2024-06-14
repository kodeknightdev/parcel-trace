using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages.Dispatch;

public sealed class RouteModel(IGatewayClient gateway, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? CourierId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Day { get; set; }

    public DateOnly Date => Day ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    public IReadOnlyList<CourierDto> Couriers { get; private set; } = [];
    public RouteDto? Route { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(DateOnly? date, CancellationToken ct)
    {
        Day = date ?? Day;
        Couriers = (await gateway.CouriersAsync(ct)).Value ?? [];
        CourierId ??= Couriers.FirstOrDefault()?.Id;
        if (CourierId is null)
        {
            return;
        }

        var result = await gateway.RouteAsync(CourierId.Value, Date, ct);
        Route = result.Value;
        Error = result.Ok ? null : result.Error;
    }
}
