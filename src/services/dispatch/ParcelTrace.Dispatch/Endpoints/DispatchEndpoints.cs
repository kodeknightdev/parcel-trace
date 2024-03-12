using System.Security.Claims;
using MongoDB.Driver;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.BuildingBlocks.Errors;
using ParcelTrace.BuildingBlocks.Validation;
using ParcelTrace.Dispatch.Data;
using ParcelTrace.Dispatch.Domain;
using ParcelTrace.Dispatch.Services;

namespace ParcelTrace.Dispatch.Endpoints;

public static class DispatchEndpoints
{
    public static RouteGroupBuilder MapDispatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dispatch").WithTags("Dispatch");

        // couriers
        group.MapGet("/routes/me", (ClaimsPrincipal user, AssignmentService service, DispatchDb db, TimeProvider clock, CancellationToken ct, DateOnly? date) =>
                RouteAsync(CourierEndpoints.MyCourierId(user), date, service, db, clock, ct))
            .RequireAuthorization(AuthPolicies.Courier).WithSummary("The signed-in courier's route for a day (default today)");

        group.MapGet("/offers/me", async (ClaimsPrincipal user, AssignmentService service, DispatchDb db, CancellationToken ct) =>
        {
            var courierId = CourierEndpoints.MyCourierId(user);
            var sagas = await service.OffersForCourierAsync(courierId, ct);
            var ids = sagas.Select(s => s.ShipmentId).ToList();
            var pending = await db.Pending.Find(p => ids.Contains(p.ShipmentId)).ToListAsync(ct);
            return Results.Ok(sagas.Select(s =>
            {
                var offer = s.CurrentOffer!;
                var p = pending.FirstOrDefault(x => x.ShipmentId == s.ShipmentId);
                return new CourierOfferDto(s.ShipmentId, s.TrackingNumber, offer.CourierName, offer.DistanceKm, offer.ExpiresAt,
                    p?.ServiceLevel ?? default, p?.PickupCity ?? "", p?.PickupPostalCode ?? "", p?.DropoffCity ?? "", p?.WeightKg ?? 0);
            }));
        }).RequireAuthorization(AuthPolicies.Courier).WithSummary("Offers waiting for the signed-in courier's answer");

        group.MapPost("/offers/{shipmentId:guid}/accept", async (Guid shipmentId, ClaimsPrincipal user, AssignmentService service, CancellationToken ct) =>
                Results.Ok(AssignmentDto.From(await service.AcceptOfferAsync(shipmentId, CourierEndpoints.MyCourierId(user), ct))))
            .RequireAuthorization(AuthPolicies.Courier).WithSummary("Accept an offer: the shipment is assigned to you");

        group.MapPost("/offers/{shipmentId:guid}/reject", async (Guid shipmentId, RejectOfferRequest? request, ClaimsPrincipal user, AssignmentService service, CancellationToken ct) =>
                Results.Ok(await service.RejectOfferAsync(shipmentId, CourierEndpoints.MyCourierId(user), request?.Reason, ct)))
            .RequireAuthorization(AuthPolicies.Courier).WithSummary("Reject an offer: it moves on to the next courier");

        group.MapGet("/assignments/{shipmentId:guid}", async (Guid shipmentId, ClaimsPrincipal user, DispatchDb db, CancellationToken ct) =>
        {
            var assignment = await db.Assignments.Find(a => a.ShipmentId == shipmentId && a.Status != AssignmentStatus.Reassigned)
                                 .SortByDescending(a => a.AssignedAt).FirstOrDefaultAsync(ct)
                             ?? throw new NotFoundException("This shipment has no courier yet.");
            if (!user.IsDispatcherOrAdmin() && user.GetCourierId() != assignment.CourierId)
            {
                throw new NotFoundException("This shipment has no courier yet.");
            }

            return Results.Ok(AssignmentDto.From(assignment));
        }).RequireAuthorization(AuthPolicies.Staff).WithSummary("Current assignment of a shipment");

        // dispatchers
        var staff = group.MapGroup("/").RequireAuthorization(AuthPolicies.Dispatcher);

        staff.MapGet("/queue", async (DispatchDb db, CancellationToken ct) =>
                Results.Ok(await db.Pending.Find(FilterDefinition<PendingShipment>.Empty)
                    .SortByDescending(p => p.Escalated).ThenByDescending(p => p.ServiceLevel).ThenBy(p => p.QueuedAt).ToListAsync(ct)))
            .WithSummary("Shipments without a courier (escalated first, then Express)");

        staff.MapGet("/escalations", async (DispatchDb db, CancellationToken ct) =>
                Results.Ok((await db.Sagas.Find(s => s.State == SagaState.Escalated).SortBy(s => s.EscalatedAt).ToListAsync(ct)).Select(SagaDto.From)))
            .WithSummary("Assignment sagas that need a dispatcher");

        staff.MapGet("/sagas/{shipmentId:guid}", async (Guid shipmentId, AssignmentService service, CancellationToken ct) =>
                Results.Ok(SagaDto.From(await service.LoadSagaAsync(shipmentId, ct) ?? throw new NotFoundException("No assignment saga for this shipment."))))
            .WithSummary("Offer history and state of a shipment's assignment saga");

        staff.MapPost("/assign", async (AssignRequest request, ClaimsPrincipal user, AssignmentService service, CancellationToken ct) =>
                Results.Ok(AssignmentDto.From(await service.AssignManualAsync(request.ShipmentId, request.CourierId, user.GetEmail(), ct))))
            .Validate<AssignRequest>().WithSummary("Assign directly (resolves escalations, re-assigns before pick-up)");

        staff.MapPost("/auto-assign", async (AutoAssignRequest request, AssignmentService service, CancellationToken ct) =>
                Results.Ok(await service.OfferAsync(request.ShipmentId, ct)))
            .WithSummary("Offer the shipment to the nearest available courier");

        staff.MapPost("/auto-assign/all", async (AssignmentService service, CancellationToken ct) =>
                Results.Ok(await service.OfferPendingAsync(ct)))
            .WithSummary("Make offers for every waiting shipment");

        staff.MapGet("/routes/{courierId:guid}", (Guid courierId, AssignmentService service, DispatchDb db, TimeProvider clock, CancellationToken ct, DateOnly? date) =>
                RouteAsync(courierId, date, service, db, clock, ct))
            .WithSummary("A courier's route for a day (default today)");

        staff.MapGet("/summary", async (DispatchDb db, CancellationToken ct) =>
        {
            var couriers = await db.Couriers.Find(FilterDefinition<Courier>.Empty).ToListAsync(ct);
            var dtos = couriers.Select(CourierDto.From).ToList();
            var pending = await db.Pending.CountDocumentsAsync(FilterDefinition<PendingShipment>.Empty, cancellationToken: ct);
            var offered = await db.Sagas.CountDocumentsAsync(s => s.State == SagaState.Offered, cancellationToken: ct);
            var escalated = await db.Sagas.CountDocumentsAsync(s => s.State == SagaState.Escalated, cancellationToken: ct);
            var active = await db.Assignments.CountDocumentsAsync(a => a.Status == AssignmentStatus.Active, cancellationToken: ct);
            return Results.Ok(new DispatchSummary(
                dtos.Count,
                dtos.Count(c => c.IsAvailable),
                dtos.Count(c => c.Availability == "Idle"),
                dtos.Count(c => c.Availability == "Busy"),
                dtos.Count(c => c.Availability == "Full"),
                dtos.Count(c => c.Availability == "OffDuty"),
                (int)pending,
                (int)offered,
                (int)escalated,
                (int)active));
        }).WithSummary("Courier availability, queue, open offers and escalations for the dashboard");

        return group;
    }

    private static async Task<IResult> RouteAsync(Guid courierId, DateOnly? date, AssignmentService service, DispatchDb db, TimeProvider clock, CancellationToken ct)
    {
        var courier = await db.Couriers.Find(c => c.Id == courierId).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException($"Courier {courierId} not found.");
        var day = date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var stops = await service.RouteAsync(courierId, day, ct);
        return Results.Ok(new RouteDto(courier.Id, courier.Name, day.ToString("yyyy-MM-dd"),
            stops.Select(AssignmentDto.From).ToList(),
            stops.Count(s => s.Status == AssignmentStatus.Active),
            stops.Count(s => s.Status != AssignmentStatus.Active)));
    }
}
