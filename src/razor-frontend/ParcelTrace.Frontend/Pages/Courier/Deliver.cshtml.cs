using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages.Courier;

public sealed class DeliverModel(IGatewayClient gateway) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Tn { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Failed { get; set; }

    [BindProperty]
    public DeliveryInput Input { get; set; } = new();

    public sealed class DeliveryInput
    {
        [StringLength(120)] public string? RecipientName { get; set; }
        [StringLength(120)] public string? SignatureName { get; set; }
        [Url, StringLength(500)] public string? PhotoUrl { get; set; }
        [StringLength(300)] public string? Reason { get; set; }
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(Guid shipmentId, CancellationToken ct)
    {
        if (!Failed && (string.IsNullOrWhiteSpace(Input.RecipientName) || string.IsNullOrWhiteSpace(Input.SignatureName)))
        {
            ModelState.AddModelError(string.Empty, "Enter who received the parcel and their typed signature.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var update = Failed
            ? new StatusUpdate("Failed", Input.Reason ?? "Delivery failed")
            : new StatusUpdate("Delivered", Proof: new ProofInput(Input.RecipientName!.Trim(), Input.SignatureName!.Trim(),
                string.IsNullOrWhiteSpace(Input.PhotoUrl) ? null : Input.PhotoUrl.Trim()));

        var result = await gateway.UpdateStatusAsync(shipmentId, update, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Update failed.");
            return Page();
        }

        TempData["Flash"] = Failed ? $"{result.Value!.TrackingNumber} reported as failed." : $"{result.Value!.TrackingNumber} delivered.";
        return RedirectToPage("Index");
    }
}
