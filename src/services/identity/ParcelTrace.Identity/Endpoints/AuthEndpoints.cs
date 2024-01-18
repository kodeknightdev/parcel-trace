using Microsoft.EntityFrameworkCore;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.BuildingBlocks.Errors;
using ParcelTrace.BuildingBlocks.Validation;
using ParcelTrace.Contracts;
using ParcelTrace.Identity.Auth;
using ParcelTrace.Identity.Data;

namespace ParcelTrace.Identity.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync).Validate<RegisterRequest>().AllowAnonymous()
            .WithSummary("Create a customer account and sign in");
        group.MapPost("/login", LoginAsync).Validate<LoginRequest>().AllowAnonymous()
            .WithSummary("Exchange email + password for a JWT");
        group.MapGet("/me", MeAsync).RequireAuthorization(AuthPolicies.Authenticated)
            .WithSummary("The signed-in user");

        return group;
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest request, IdentityDbContext db, IPasswordHasher hasher, TokenService tokens, CancellationToken ct)
    {
        var normalized = User.Normalize(request.Email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = hasher.Hash(request.Password),
            Role = Roles.Customer,
            LastLoginAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var token = tokens.Issue(user);
        return Results.Created("/api/auth/me", new AuthResponse(token.AccessToken, token.ExpiresAt, UserDto.From(user)));
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, IdentityDbContext db, IPasswordHasher hasher, TokenService tokens, CancellationToken ct)
    {
        var normalized = User.Normalize(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null || !user.IsActive || !hasher.Verify(request.Password, user.PasswordHash))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials",
                detail: "Email or password is incorrect.");
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var token = tokens.Issue(user);
        return Results.Ok(new AuthResponse(token.AccessToken, token.ExpiresAt, UserDto.From(user)));
    }

    private static async Task<IResult> MeAsync(HttpContext http, IdentityDbContext db, CancellationToken ct)
    {
        var id = http.User.GetUserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct)
                   ?? throw new NotFoundException("User no longer exists.");
        return Results.Ok(UserDto.From(user));
    }
}
