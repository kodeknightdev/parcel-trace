using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ParcelTrace.Contracts;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Pages.Admin;

public sealed class UsersModel(IGatewayClient gateway) : PageModel
{
    public IReadOnlyList<UserDto> Users { get; private set; } = [];
    public IReadOnlyList<CourierDto> Couriers { get; private set; } = [];

    [BindProperty]
    public UserInput Input { get; set; } = new();

    public sealed class UserInput
    {
        [Required, StringLength(120)] public string DisplayName { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, MinLength(8)] public string Password { get; set; } = string.Empty;
        [Required] public string Role { get; set; } = Roles.Dispatcher;
        public Guid? CourierId { get; set; }
    }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await gateway.CreateUserAsync(new CreateUserRequest(Input.Email, Input.Password, Input.DisplayName, Input.Role,
                Input.Role == Roles.Courier ? Input.CourierId : null), ct);
            if (result.Ok)
            {
                TempData["Flash"] = $"{result.Value!.DisplayName} can now sign in as {result.Value.Role}.";
                return RedirectToPage();
            }

            ModelState.AddModelError(string.Empty, result.Error ?? "Could not create the account.");
        }

        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var users = gateway.UsersAsync(ct);
        var couriers = gateway.CouriersAsync(ct);
        await Task.WhenAll(users, couriers);
        Users = (await users).Value ?? [];
        Couriers = (await couriers).Value ?? [];
    }
}
