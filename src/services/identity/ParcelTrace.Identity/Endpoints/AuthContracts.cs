using FluentValidation;
using ParcelTrace.Contracts;
using ParcelTrace.Identity.Data;

namespace ParcelTrace.Identity.Endpoints;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record CreateUserRequest(string Email, string Password, string DisplayName, string Role, Guid? CourierId);

public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role, Guid? CourierId, bool IsActive, DateTimeOffset CreatedAt)
{
    public static UserDto From(User u) => new(u.Id, u.Email, u.DisplayName, u.Role, u.CourierId, u.IsActive, u.CreatedAt);
}

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

internal static class UserRules
{
    public static void Email<T>(this IRuleBuilder<T, string> rule) => rule.NotEmpty().EmailAddress().MaximumLength(256);

    public static void Password<T>(this IRuleBuilder<T, string> rule) => rule.NotEmpty().MinimumLength(8).MaximumLength(128)
        .Matches("[0-9]").WithMessage("Password must contain a digit.")
        .Matches("[A-Za-z]").WithMessage("Password must contain a letter.");
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).Email();
        RuleFor(x => x.Password).Password();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).Email();
        RuleFor(x => x.Password).Password();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Role).Must(r => Roles.All.Contains(r)).WithMessage($"Role must be one of {string.Join(", ", Roles.All)}.");
        RuleFor(x => x.CourierId).NotNull().When(x => x.Role == Roles.Courier).WithMessage("Couriers need a courierId from the dispatch registry.");
        RuleFor(x => x.CourierId).Null().When(x => x.Role != Roles.Courier).WithMessage("Only couriers have a courierId.");
    }
}
