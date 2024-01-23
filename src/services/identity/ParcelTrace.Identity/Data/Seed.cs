using Microsoft.EntityFrameworkCore;
using ParcelTrace.Contracts;
using ParcelTrace.Identity.Auth;

namespace ParcelTrace.Identity.Data;

public static class Seed
{
    public const string PasswordSetting = "SEED_DEMO_PASSWORD";

    public static readonly Guid CustomerId = Guid.Parse("a11ce000-0000-4000-8000-000000000001");
    public static readonly Guid Customer2Id = Guid.Parse("b0b00000-0000-4000-8000-000000000002");

    private static readonly (Guid? Id, string Email, string Name, string Role, Guid? CourierId)[] DemoUsers =
    [
        (null, "admin@parceltrace.local", "Ada Admin", Roles.Admin, null),
        (null, "dispatcher@parceltrace.local", "Dana Dispatcher", Roles.Dispatcher, null),
        (CustomerId, "customer@parceltrace.local", "Alice Moreau", Roles.Customer, null),
        (Customer2Id, "customer2@parceltrace.local", "Bob Lindqvist", Roles.Customer, null),
        (null, "courier1@parceltrace.local", "Max Keller", Roles.Courier, Guid.Parse("c0de0000-0000-4000-8000-000000000001")),
        (null, "courier2@parceltrace.local", "Lena Fischer", Roles.Courier, Guid.Parse("c0de0000-0000-4000-8000-000000000002")),
        (null, "courier3@parceltrace.local", "Tom Becker", Roles.Courier, Guid.Parse("c0de0000-0000-4000-8000-000000000003")),
        (null, "courier4@parceltrace.local", "Sara Wolf", Roles.Courier, Guid.Parse("c0de0000-0000-4000-8000-000000000004")),
    ];

    public static async Task RunAsync(IdentityDbContext db, IPasswordHasher hasher, IConfiguration configuration, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var password = configuration[PasswordSetting];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("{Setting} is not set; skipping demo accounts", PasswordSetting);
            return;
        }

        var hash = hasher.Hash(password);
        foreach (var (id, email, name, role, courierId) in DemoUsers)
        {
            db.Users.Add(new User
            {
                Id = id ?? Guid.NewGuid(),
                Email = email,
                NormalizedEmail = User.Normalize(email),
                DisplayName = name,
                Role = role,
                CourierId = courierId,
                PasswordHash = hash
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} demo users", DemoUsers.Length);
    }
}
