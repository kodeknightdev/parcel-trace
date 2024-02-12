using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.BuildingBlocks.Errors;
using ParcelTrace.BuildingBlocks.Validation;
using ParcelTrace.Contracts;
using ParcelTrace.Shipments.Data;
using ParcelTrace.Shipments.Domain;

namespace ParcelTrace.Shipments.Endpoints;

public static class ShipmentEndpoints
{
    public static RouteGroupBuilder MapShipmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shipments").WithTags("Shipments");

        group.MapPost("/quote", (QuoteRequest request, QuoteCalculator calculator) =>
                Results.Ok(calculator.Calculate(new QuoteInput(request.Pickup, request.Dropoff, request.Package.WeightKg,
                    request.Package.LengthCm, request.Package.WidthCm, request.Package.HeightCm, request.ServiceLevel))))
            .Validate<QuoteRequest>().AllowAnonymous().WithSummary("Price quote for a parcel");

        group.MapGet("/track/{trackingNumber}", TrackAsync).AllowAnonymous()
            .WithSummary("Public shipment summary by tracking number");

        group.MapPost("/", CreateAsync).Validate<CreateShipmentRequest>().RequireAuthorization(AuthPolicies.Customer)
            .WithSummary("Create a shipment and get a tracking number");

        group.MapGet("/mine", MineAsync).RequireAuthorization(AuthPolicies.Customer)
            .WithSummary("The signed-in customer's shipments, newest first");

        group.MapGet("/", ListAsync).RequireAuthorization(AuthPolicies.Dispatcher)
            .WithSummary("All shipments with filters (dispatchers)");

        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(AuthPolicies.Authenticated)
            .WithSummary("Shipment details (owner, assigned courier or dispatcher)");

        group.MapPost("/{id:guid}/cancel", CancelAsync).RequireAuthorization(AuthPolicies.Authenticated)
            .WithSummary("Cancel within the cancellation window, before pick-up");

        return group;
    }

    private static async Task<IResult> CreateAsync(CreateShipmentRequest request, ClaimsPrincipal user, ShipmentService service, TimeProvider clock, CancellationToken ct)
    {
        var shipment = await service.CreateAsync(request, user.GetUserId(), user.GetEmail(), ct);
        return Results.Created($"/api/shipments/{shipment.Id}", ShipmentDto.From(shipment, clock.GetUtcNow()));
    }

    private static async Task<IResult> MineAsync(ClaimsPrincipal user, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct, int take = 50)
    {
        var customerId = user.GetUserId();
        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => s.CustomerId == customerId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        var now = clock.GetUtcNow();
        return Results.Ok(shipments.Select(s => ShipmentDto.From(s, now)));
    }

    private static async Task<IResult> ListAsync(ShipmentsDbContext db, TimeProvider clock, CancellationToken ct,
        ShipmentStatus? status = null, bool? unassigned = null, Guid? courierId = null, string? q = null, int page = 1, int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Shipments.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(s => s.Status == status);
        }

        if (unassigned == true)
        {
            query = query.Where(s => s.Status == ShipmentStatus.Created);
        }

        if (courierId is not null)
        {
            query = query.Where(s => s.CourierId == courierId);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToUpperInvariant();
            query = query.Where(s => s.TrackingNumber.Contains(term) || s.Dropoff.City.ToUpper().Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(s => s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var now = clock.GetUtcNow();
        return Results.Ok(new ShipmentPage(items.Select(s => ShipmentDto.From(s, now)).ToList(), page, pageSize, total));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var shipment = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct)
                       ?? throw new NotFoundException($"Shipment {id} not found.");
        EnsureCanView(user, shipment);
        return Results.Ok(ShipmentDto.From(shipment, clock.GetUtcNow()));
    }

    private static async Task<IResult> CancelAsync(Guid id, CancelShipmentRequest? request, ClaimsPrincipal user, ShipmentsDbContext db,
        ShipmentService service, TimeProvider clock, CancellationToken ct)
    {
        var shipment = await db.Shipments.SingleOrDefaultAsync(s => s.Id == id, ct)
                       ?? throw new NotFoundException($"Shipment {id} not found.");
        if (shipment.CustomerId != user.GetUserId() && !user.IsDispatcherOrAdmin())
        {
            throw new ForbiddenException("Only the customer who booked the shipment or a dispatcher can cancel it.");
        }

        try
        {
            await service.CancelAsync(shipment, request?.Reason, ct);
        }
        catch (ShipmentRuleException ex)
        {
            throw new ConflictException(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The shipment changed while cancelling (it may have just been picked up). Reload and try again.");
        }

        return Results.Ok(ShipmentDto.From(shipment, clock.GetUtcNow()));
    }

    private static async Task<IResult> TrackAsync(string trackingNumber, ShipmentsDbContext db, CancellationToken ct)
    {
        var normalized = trackingNumber.Trim().ToUpperInvariant();
        if (!TrackingNumberGenerator.IsValid(normalized))
        {
            throw new BadRequestException("Tracking numbers look like PT-2024-ABC234.");
        }

        var s = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(x => x.TrackingNumber == normalized, ct)
                ?? throw new NotFoundException($"No shipment with tracking number {normalized}.");
        return Results.Ok(new PublicShipmentDto(s.TrackingNumber, s.Status, s.ServiceLevel, s.Pickup.City, s.Dropoff.City,
            s.CreatedAt, s.SlaDueAt, s.EtaAt, s.DeliveredAt, s.Proof?.RecipientName));
    }

    private static void EnsureCanView(ClaimsPrincipal user, Shipment shipment)
    {
        if (user.IsDispatcherOrAdmin())
        {
            return;
        }

        if (user.IsInRole(Roles.Courier) && user.GetCourierId() is { } courierId && shipment.CourierId == courierId)
        {
            return;
        }

        if (user.IsInRole(Roles.Customer) && shipment.CustomerId == user.GetUserId())
        {
            return;
        }

        // 404, not 403, so ids can't be probed
        throw new NotFoundException($"Shipment {shipment.Id} not found.");
    }
}
