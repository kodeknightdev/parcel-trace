using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ParcelTrace.Contracts;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages.Shipments;

public sealed class CreateModel(IGatewayClient gateway) : PageModel
{
    [BindProperty]
    public ShipmentInput Input { get; set; } = new();

    public Quote? Preview { get; private set; }

    public sealed class ShipmentInput
    {
        [Required, StringLength(120)] public string SenderName { get; set; } = string.Empty;
        [Required, Phone] public string SenderPhone { get; set; } = string.Empty;
        [Required, StringLength(200)] public string PickupLine1 { get; set; } = string.Empty;
        [Required] public string PickupPostalCode { get; set; } = string.Empty;
        [Required] public string PickupCity { get; set; } = "Berlin";
        [Range(-90, 90)] public double PickupLat { get; set; } = 52.5219;
        [Range(-180, 180)] public double PickupLng { get; set; } = 13.4132;

        [Required, StringLength(120)] public string RecipientName { get; set; } = string.Empty;
        [Required, Phone] public string RecipientPhone { get; set; } = string.Empty;
        [Required, StringLength(200)] public string DropoffLine1 { get; set; } = string.Empty;
        [Required] public string DropoffPostalCode { get; set; } = string.Empty;
        [Required] public string DropoffCity { get; set; } = "Berlin";
        [Range(-90, 90)] public double DropoffLat { get; set; } = 52.5030;
        [Range(-180, 180)] public double DropoffLng { get; set; } = 13.3320;

        [Range(0.1, 30)] public decimal WeightKg { get; set; } = 1;
        [Range(1, 150)] public int LengthCm { get; set; } = 30;
        [Range(1, 150)] public int WidthCm { get; set; } = 20;
        [Range(1, 150)] public int HeightCm { get; set; } = 10;
        public ServiceLevel ServiceLevel { get; set; } = ServiceLevel.Standard;
        [StringLength(500)] public string? Notes { get; set; }

        public CreateShipmentRequest ToRequest() => new(
            new PartyDto(SenderName, SenderPhone),
            new PartyDto(RecipientName, RecipientPhone),
            new AddressDto(PickupLine1, PickupCity, PickupPostalCode, "DE", PickupLat, PickupLng),
            new AddressDto(DropoffLine1, DropoffCity, DropoffPostalCode, "DE", DropoffLat, DropoffLng),
            new PackageDto(WeightKg, LengthCm, WidthCm, HeightCm),
            ServiceLevel,
            string.IsNullOrWhiteSpace(Notes) ? null : Notes);
    }

    public void OnGet()
    {
        Input.SenderName = User.Identity?.Name ?? string.Empty;
    }

    public async Task<IActionResult> OnPostQuoteAsync(CancellationToken ct)
    {
        ModelState.Clear(); // price check only needs the parcel fields
        var result = await gateway.QuoteAsync(new QuoteRequest(
            new GeoPoint(Input.PickupLat, Input.PickupLng), new GeoPoint(Input.DropoffLat, Input.DropoffLng),
            new PackageDto(Input.WeightKg, Input.LengthCm, Input.WidthCm, Input.HeightCm), Input.ServiceLevel), ct);
        if (result.Ok)
        {
            Preview = result.Value;
        }
        else
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not calculate a price.");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await gateway.CreateShipmentAsync(Input.ToRequest(), ct);
        if (!result.Ok)
        {
            foreach (var (field, messages) in result.ValidationErrors ?? new Dictionary<string, string[]>())
            {
                foreach (var message in messages)
                {
                    ModelState.AddModelError(string.Empty, $"{field}: {message}");
                }
            }

            if (result.ValidationErrors is null)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Booking failed.");
            }

            return Page();
        }

        TempData["Flash"] = $"Booked! Your tracking number is {result.Value!.TrackingNumber}.";
        return RedirectToPage("Details", new { id = result.Value.Id });
    }
}
