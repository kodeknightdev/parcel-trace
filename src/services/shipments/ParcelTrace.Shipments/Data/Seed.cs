using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParcelTrace.Contracts;
using ParcelTrace.Shipments.Domain;
using ParcelTrace.Shipments.Endpoints;

namespace ParcelTrace.Shipments.Data;

public static class Seed
{
    private static readonly Guid CustomerId = Guid.Parse("a11ce000-0000-4000-8000-000000000001");
    private static readonly Guid Customer2Id = Guid.Parse("b0b00000-0000-4000-8000-000000000002");

    private static readonly AddressDto Alexanderplatz = new("Alexanderplatz 1", "Berlin", "10178", "DE", 52.5219, 13.4132);
    private static readonly AddressDto Kudamm = new("Kurfürstendamm 21", "Berlin", "10719", "DE", 52.5030, 13.3320);
    private static readonly AddressDto Friedrichstrasse = new("Friedrichstraße 43", "Berlin", "10117", "DE", 52.5070, 13.3903);
    private static readonly AddressDto Schoenhauser = new("Schönhauser Allee 36", "Berlin", "10435", "DE", 52.5392, 13.4123);
    private static readonly AddressDto KarlMarx = new("Karl-Marx-Straße 66", "Berlin", "12043", "DE", 52.4810, 13.4350);
    private static readonly AddressDto Tempelhof = new("Tempelhofer Damm 2", "Berlin", "12101", "DE", 52.4722, 13.3856);
    private static readonly AddressDto Wedding = new("Müllerstraße 146", "Berlin", "13353", "DE", 52.5480, 13.3530);
    private static readonly AddressDto Friedrichshain = new("Frankfurter Allee 71", "Berlin", "10247", "DE", 52.5145, 13.4710);

    public static async Task RunAsync(IServiceProvider services, ILogger logger, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<ShipmentsDbContext>();
        if (await db.Shipments.AnyAsync(ct))
        {
            return;
        }

        var service = new ShipmentService(db,
            services.GetRequiredService<QuoteCalculator>(),
            services.GetRequiredService<ITrackingNumberGenerator>(),
            services.GetRequiredService<IOptions<ShipmentOptions>>(),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<ILogger<ShipmentService>>());

        var alice = new PartyDto("Alice Moreau", "+49 30 1234567", "customer@parceltrace.local");
        var bob = new PartyDto("Bob Lindqvist", "+49 30 7654321", "customer2@parceltrace.local");

        (Guid Customer, string Email, CreateShipmentRequest Request)[] demo =
        [
            (CustomerId, "customer@parceltrace.local", new(alice, new PartyDto("Jonas Weber", "+49 151 2345678"), Alexanderplatz, Kudamm,
                new PackageDto(1.2m, 30, 20, 10), ServiceLevel.Standard, "Books, please don't bend")),
            (CustomerId, "customer@parceltrace.local", new(alice, new PartyDto("Mia Schneider", "+49 160 9876543"), Alexanderplatz, Schoenhauser,
                new PackageDto(0.4m, 20, 15, 5), ServiceLevel.Express, null)),
            (CustomerId, "customer@parceltrace.local", new(alice, new PartyDto("Lukas Hoffmann", "+49 170 1112233"), Friedrichstrasse, Tempelhof,
                new PackageDto(6.5m, 50, 40, 30), ServiceLevel.Standard, "Ring twice")),
            (Customer2Id, "customer2@parceltrace.local", new(bob, new PartyDto("Emma Wagner", "+49 152 4445566"), Wedding, Friedrichshain,
                new PackageDto(2.0m, 35, 25, 15), ServiceLevel.Express, null)),
            (Customer2Id, "customer2@parceltrace.local", new(bob, new PartyDto("Noah Becker", "+49 157 7778899"), KarlMarx, Wedding,
                new PackageDto(12m, 60, 40, 40), ServiceLevel.Standard, "Leave with neighbour if not home")),
            (Customer2Id, "customer2@parceltrace.local", new(bob, new PartyDto("Hannah Koch", "+49 159 3332211"), Kudamm, Friedrichstrasse,
                new PackageDto(0.8m, 25, 18, 8), ServiceLevel.Standard, null)),
        ];

        foreach (var (customer, email, request) in demo)
        {
            await service.CreateAsync(request, customer, email, ct);
        }

        logger.LogInformation("Seeded {Count} demo shipments", demo.Length);
    }
}
