using MongoDB.Driver;
using ParcelTrace.Contracts;
using ParcelTrace.Dispatch.Domain;

namespace ParcelTrace.Dispatch.Data;

public static class Seed
{
    public static readonly Courier[] Couriers =
    [
        new()
        {
            Id = Guid.Parse("c0de0000-0000-4000-8000-000000000001"), Name = "Max Keller", Phone = "+49 151 0000001",
            VehicleType = VehicleType.Van, CapacityKg = 400, MaxActiveShipments = 12, IsAvailable = true,
            HomeBase = Position.From(52.5200, 13.4050) // Mitte
        },
        new()
        {
            Id = Guid.Parse("c0de0000-0000-4000-8000-000000000002"), Name = "Lena Fischer", Phone = "+49 151 0000002",
            VehicleType = VehicleType.Car, CapacityKg = 150, MaxActiveShipments = 8, IsAvailable = true,
            HomeBase = Position.From(52.4986, 13.4030) // Kreuzberg
        },
        new()
        {
            Id = Guid.Parse("c0de0000-0000-4000-8000-000000000003"), Name = "Tom Becker", Phone = "+49 151 0000003",
            VehicleType = VehicleType.Scooter, CapacityKg = 30, MaxActiveShipments = 5, IsAvailable = true,
            HomeBase = Position.From(52.5380, 13.4240) // Prenzlauer Berg
        },
        new()
        {
            Id = Guid.Parse("c0de0000-0000-4000-8000-000000000004"), Name = "Sara Wolf", Phone = "+49 151 0000004",
            VehicleType = VehicleType.Bike, CapacityKg = 15, MaxActiveShipments = 4, IsAvailable = false,
            HomeBase = Position.From(52.5163, 13.3041) // Charlottenburg
        }
    ];

    public static async Task RunAsync(DispatchDb db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Couriers.EstimatedDocumentCountAsync(cancellationToken: ct) > 0)
        {
            return;
        }

        await db.Couriers.InsertManyAsync(Couriers, cancellationToken: ct);
        logger.LogInformation("Seeded {Count} demo couriers", Couriers.Length);
    }
}
