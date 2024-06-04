using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages.Account;

public sealed class RegisterModel(IGatewayClient gateway, ICookieSignIn signIn) : PageModel
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    public sealed class RegisterInput
    {
        [Required, StringLength(120)] public string DisplayName { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, MinLength(8), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await gateway.RegisterAsync(Input.Email, Input.Password, Input.DisplayName, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Registration failed.");
            return Page();
        }

        await signIn.SignInAsync(HttpContext, result.Value!);
        TempData["Flash"] = $"Welcome, {result.Value!.User.DisplayName}!";
        return RedirectToPage("/Shipments/Create");
    }
}
