using Microsoft.EntityFrameworkCore;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.Contracts;
using ParcelTrace.Shipments.Data;
using ParcelTrace.Shipments.Domain;

namespace ParcelTrace.Shipments.Endpoints;

public static class DashboardEndpoints
{
    private static readonly ShipmentStatus[] InProgress =
        [ShipmentStatus.Assigned, ShipmentStatus.PickedUp, ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery];

    public static RouteGroupBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shipments/dashboard").WithTags("Dashboard").RequireAuthorization(AuthPolicies.Dispatcher);

        group.MapGet("/daily", DailyAsync).WithSummary("Today's counts (UTC day) for the dispatcher dashboard");
        group.MapGet("/delayed", DelayedAsync).WithSummary("Open shipments past their SLA, or whose ETA is past it");

        return group;
    }

    private static async Task<IResult> DailyAsync(ShipmentsDbContext db, TimeProvider clock, CancellationToken ct, DateOnly? date = null)
    {
        var now = clock.GetUtcNow();
        var day = date ?? DateOnly.FromDateTime(now.UtcDateTime);
        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = from.AddDays(1);

        var created = await db.Shipments.CountAsync(s => s.CreatedAt >= from && s.CreatedAt < to, ct);
        var delivered = await db.Shipments.CountAsync(s => s.DeliveredAt >= from && s.DeliveredAt < to, ct);
        var failed = await db.Shipments.CountAsync(s => s.Status == ShipmentStatus.Failed && s.UpdatedAt >= from && s.UpdatedAt < to, ct);
        var cancelled = await db.Shipments.CountAsync(s => s.CancelledAt >= from && s.CancelledAt < to, ct);
        var unassigned = await db.Shipments.CountAsync(s => s.Status == ShipmentStatus.Created, ct);
        var inProgress = await db.Shipments.CountAsync(s => InProgress.Contains(s.Status), ct);
        var delayed = await DelayedQuery(db, now).CountAsync(ct);

        return Results.Ok(new DailyCounts(day, created, delivered, failed, cancelled, unassigned, inProgress, delayed));
    }

    private static async Task<IResult> DelayedAsync(ShipmentsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var items = await DelayedQuery(db, now).OrderBy(s => s.SlaDueAt).Take(100).ToListAsync(ct);
        return Results.Ok(items.Select(s => ShipmentDto.From(s, now)));
    }

    private static IQueryable<Shipment> DelayedQuery(ShipmentsDbContext db, DateTimeOffset now) =>
        db.Shipments.AsNoTracking().Where(s =>
            s.Status != ShipmentStatus.Delivered && s.Status != ShipmentStatus.Failed && s.Status != ShipmentStatus.Cancelled &&
            (s.SlaDueAt < now || (s.EtaAt != null && s.EtaAt > s.SlaDueAt)));
}
