using System.Security.Claims;
using MongoDB.Driver;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.BuildingBlocks.Errors;
using ParcelTrace.BuildingBlocks.Validation;
using ParcelTrace.Dispatch.Data;
using ParcelTrace.Dispatch.Domain;

namespace ParcelTrace.Dispatch.Endpoints;

public static class CourierEndpoints
{
    public static RouteGroupBuilder MapCourierEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/couriers").WithTags("Couriers");

        group.MapGet("/me", async (ClaimsPrincipal user, DispatchDb db, CancellationToken ct) =>
                Results.Ok(CourierDto.From(await FindAsync(db, MyCourierId(user), ct))))
            .RequireAuthorization(AuthPolicies.Courier).WithSummary("The signed-in courier's record");

        group.MapPut("/me/availability", (AvailabilityRequest request, ClaimsPrincipal user, DispatchDb db, CancellationToken ct) =>
                SetAvailabilityAsync(MyCourierId(user), request.IsAvailable, db, ct))
            .RequireAuthorization(AuthPolicies.Courier).WithSummary("Go on or off duty");

        var staff = group.MapGroup("/").RequireAuthorization(AuthPolicies.Dispatcher);

        staff.MapGet("/", async (DispatchDb db, CancellationToken ct, bool? available = null) =>
        {
            var filter = available is null ? FilterDefinition<Courier>.Empty : Builders<Courier>.Filter.Eq(c => c.IsAvailable, available.Value);
            var couriers = await db.Couriers.Find(filter).SortBy(c => c.Name).ToListAsync(ct);
            return Results.Ok(couriers.Select(CourierDto.From));
        }).WithSummary("Courier registry with live availability and load");

        staff.MapGet("/{id:guid}", async (Guid id, DispatchDb db, CancellationToken ct) => Results.Ok(CourierDto.From(await FindAsync(db, id, ct))))
            .WithSummary("One courier");

        staff.MapPost("/", async (CourierRequest request, DispatchDb db, CancellationToken ct) =>
        {
            var courier = new Courier
            {
                Name = request.Name.Trim(),
                Phone = request.Phone.Trim(),
                VehicleType = request.VehicleType,
                CapacityKg = request.CapacityKg,
                MaxActiveShipments = request.MaxActiveShipments,
                HomeBase = Position.From(request.HomeBase.Lat, request.HomeBase.Lng),
                IsAvailable = false
            };
            await db.Couriers.InsertOneAsync(courier, cancellationToken: ct);
            return Results.Created($"/api/couriers/{courier.Id}", CourierDto.From(courier));
        }).Validate<CourierRequest>().WithSummary("Register a courier (starts off duty)");

        staff.MapPut("/{id:guid}", async (Guid id, CourierRequest request, DispatchDb db, CancellationToken ct) =>
        {
            var courier = await db.Couriers.FindOneAndUpdateAsync(Builders<Courier>.Filter.Eq(c => c.Id, id),
                Builders<Courier>.Update
                    .Set(c => c.Name, request.Name.Trim())
                    .Set(c => c.Phone, request.Phone.Trim())
                    .Set(c => c.VehicleType, request.VehicleType)
                    .Set(c => c.CapacityKg, request.CapacityKg)
                    .Set(c => c.MaxActiveShipments, request.MaxActiveShipments)
                    .Set(c => c.HomeBase, Position.From(request.HomeBase.Lat, request.HomeBase.Lng)),
                new FindOneAndUpdateOptions<Courier, Courier> { ReturnDocument = ReturnDocument.After }, ct)
                ?? throw new NotFoundException($"Courier {id} not found.");
            return Results.Ok(CourierDto.From(courier));
        }).Validate<CourierRequest>().WithSummary("Update vehicle, capacity or home base");

        staff.MapPut("/{id:guid}/availability", (Guid id, AvailabilityRequest request, DispatchDb db, CancellationToken ct) =>
                SetAvailabilityAsync(id, request.IsAvailable, db, ct))
            .WithSummary("Put a courier on or off duty");

        return group;
    }

    private static async Task<IResult> SetAvailabilityAsync(Guid id, bool available, DispatchDb db, CancellationToken ct)
    {
        var courier = await db.Couriers.FindOneAndUpdateAsync(Builders<Courier>.Filter.Eq(c => c.Id, id),
            Builders<Courier>.Update.Set(c => c.IsAvailable, available),
            new FindOneAndUpdateOptions<Courier, Courier> { ReturnDocument = ReturnDocument.After }, ct)
            ?? throw new NotFoundException($"Courier {id} not found.");
        return Results.Ok(CourierDto.From(courier));
    }

    private static async Task<Courier> FindAsync(DispatchDb db, Guid id, CancellationToken ct) =>
        await db.Couriers.Find(c => c.Id == id).FirstOrDefaultAsync(ct) ?? throw new NotFoundException($"Courier {id} not found.");

    internal static Guid MyCourierId(ClaimsPrincipal user) =>
        user.GetCourierId() ?? throw new ForbiddenException("This login isn't linked to a courier.");
}
