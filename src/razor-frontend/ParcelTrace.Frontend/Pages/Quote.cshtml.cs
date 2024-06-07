using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ParcelTrace.Contracts;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages;

public sealed class QuoteModel(IGatewayClient gateway) : PageModel
{
    public static readonly IReadOnlyDictionary<string, GeoPoint> Places = new Dictionary<string, GeoPoint>
    {
        ["Alexanderplatz (Mitte)"] = new(52.5219, 13.4132),
        ["Kurfürstendamm (Charlottenburg)"] = new(52.5030, 13.3320),
        ["Friedrichstraße (Mitte)"] = new(52.5070, 13.3903),
        ["Schönhauser Allee (Prenzlauer Berg)"] = new(52.5392, 13.4123),
        ["Karl-Marx-Straße (Neukölln)"] = new(52.4810, 13.4350),
        ["Tempelhofer Damm (Tempelhof)"] = new(52.4722, 13.3856),
        ["Müllerstraße (Wedding)"] = new(52.5480, 13.3530),
        ["Frankfurter Allee (Friedrichshain)"] = new(52.5145, 13.4710),
        ["Potsdam Hauptbahnhof"] = new(52.3917, 13.0670),
    };

    public static IEnumerable<SelectListItem> PlaceOptions => Places.Keys.Select(k => new SelectListItem(k, k));

    [BindProperty]
    public QuoteInput Input { get; set; } = new();

    public Quote? Result { get; private set; }

    public sealed class QuoteInput
    {
        [Required] public string From { get; set; } = "Alexanderplatz (Mitte)";
        [Required] public string To { get; set; } = "Kurfürstendamm (Charlottenburg)";
        [Range(0.1, 30)] public decimal WeightKg { get; set; } = 1;
        [Range(1, 150)] public int LengthCm { get; set; } = 30;
        [Range(1, 150)] public int WidthCm { get; set; } = 20;
        [Range(1, 150)] public int HeightCm { get; set; } = 10;
        public ServiceLevel ServiceLevel { get; set; } = ServiceLevel.Standard;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var from = Places.GetValueOrDefault(Input.From);
        var to = Places.GetValueOrDefault(Input.To);
        if (from is null || to is null)
        {
            ModelState.AddModelError(string.Empty, "Pick a place from the list.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await gateway.QuoteAsync(new QuoteRequest(from!, to!,
            new PackageDto(Input.WeightKg, Input.LengthCm, Input.WidthCm, Input.HeightCm), Input.ServiceLevel), ct);
        if (result.Ok)
        {
            Result = result.Value;
        }
        else
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not calculate a quote.");
        }

        return Page();
    }
}
