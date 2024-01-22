using Microsoft.EntityFrameworkCore;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.BuildingBlocks.Errors;
using ParcelTrace.BuildingBlocks.Validation;
using ParcelTrace.Identity.Auth;
using ParcelTrace.Identity.Data;

namespace ParcelTrace.Identity.Endpoints;

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization(AuthPolicies.Admin);

        group.MapGet("/", ListAsync).WithSummary("List users, optionally filtered by role");
        group.MapPost("/", CreateAsync).Validate<CreateUserRequest>().WithSummary("Create a staff or customer account");
        group.MapPost("/{id:guid}/deactivate", (Guid id, IdentityDbContext db, CancellationToken ct) => SetActiveAsync(id, false, db, ct))
            .WithSummary("Block a user from signing in");
        group.MapPost("/{id:guid}/activate", (Guid id, IdentityDbContext db, CancellationToken ct) => SetActiveAsync(id, true, db, ct))
            .WithSummary("Allow a user to sign in again");

        return group;
    }

    private static async Task<IResult> ListAsync(string? role, IdentityDbContext db, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(u => u.Role == role);
        }

        var users = await query.OrderBy(u => u.Role).ThenBy(u => u.DisplayName).ToListAsync(ct);
        return Results.Ok(users.Select(UserDto.From));
    }

    private static async Task<IResult> CreateAsync(CreateUserRequest request, IdentityDbContext db, IPasswordHasher hasher, CancellationToken ct)
    {
        var normalized = User.Normalize(request.Email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
        {
            throw new ConflictException("An account with this email already exists.");
        }

        if (request.CourierId is { } courierId && await db.Users.AnyAsync(u => u.CourierId == courierId, ct))
        {
            throw new ConflictException("Another login is already linked to this courier.");
        }

        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = hasher.Hash(request.Password),
            Role = request.Role,
            CourierId = request.CourierId
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/users/{user.Id}", UserDto.From(user));
    }

    private static async Task<IResult> SetActiveAsync(Guid id, bool active, IdentityDbContext db, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct) ?? throw new NotFoundException($"User {id} not found.");
        user.IsActive = active;
        await db.SaveChangesAsync(ct);
        return Results.Ok(UserDto.From(user));
    }
}
