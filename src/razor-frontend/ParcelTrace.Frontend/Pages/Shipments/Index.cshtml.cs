using Microsoft.AspNetCore.Mvc.RazorPages;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages.Shipments;

public sealed class IndexModel(IGatewayClient gateway) : PageModel
{
    public IReadOnlyList<ShipmentDto> Shipments { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var result = await gateway.MyShipmentsAsync(ct);
        if (result.Ok)
        {
            Shipments = result.Value!;
        }
        else
        {
            Error = result.Error;
        }
    }
}
