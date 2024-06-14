using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ParcelTrace.Contracts;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages.Dispatch;

public sealed class CouriersModel(IGatewayClient gateway) : PageModel
{
    public IReadOnlyList<CourierDto> Couriers { get; private set; } = [];

    [BindProperty]
    public CourierInput Input { get; set; } = new();

    public sealed class CourierInput
    {
        [Required, StringLength(120)] public string Name { get; set; } = string.Empty;
        [Required, Phone] public string Phone { get; set; } = string.Empty;
        public VehicleType VehicleType { get; set; } = VehicleType.Bike;
        [Range(1, 2000)] public double CapacityKg { get; set; } = 15;
        [Range(1, 50)] public int MaxActiveShipments { get; set; } = 4;
        [Range(-90, 90)] public double HomeLat { get; set; } = 52.52;
        [Range(-180, 180)] public double HomeLng { get; set; } = 13.405;
    }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }

        var result = await gateway.CreateCourierAsync(new CourierRequest(Input.Name, Input.Phone, Input.VehicleType, Input.CapacityKg,
            Input.MaxActiveShipments, new GeoPoint(Input.HomeLat, Input.HomeLng)), ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not add the courier.");
            await LoadAsync(ct);
            return Page();
        }

        TempData["Flash"] = $"{result.Value!.Name} added (id {result.Value.Id}).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAvailabilityAsync(Guid courierId, bool available, CancellationToken ct)
    {
        var result = await gateway.SetCourierAvailabilityAsync(courierId, available, ct);
        TempData[result.Ok ? "Flash" : "FlashError"] = result.Ok
            ? $"{result.Value!.Name} is now {(available ? "on duty" : "off duty")}."
            : result.Error;
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var result = await gateway.CouriersAsync(ct);
        Couriers = result.Value ?? [];
    }
}
