using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ParcelTrace.Contracts;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages.Account;

public sealed class LoginModel(IGatewayClient gateway, ICookieSignIn signIn) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public sealed class LoginInput
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
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

        var result = await gateway.LoginAsync(Input.Email, Input.Password, ct);
        if (!result.Ok)
        {
            ModelState.AddModelError(string.Empty, result.StatusCode == 401 ? "Email or password is incorrect." : result.Error ?? "Sign-in failed.");
            return Page();
        }

        await signIn.SignInAsync(HttpContext, result.Value!);

        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return RedirectToPage(HomeFor(result.Value!.User.Role));
    }

    public static string HomeFor(string role) => role switch
    {
        Roles.Dispatcher => "/Dispatch/Index",
        Roles.Courier => "/Courier/Index",
        Roles.Admin => "/Dispatch/Index",
        _ => "/Shipments/Index"
    };
}
