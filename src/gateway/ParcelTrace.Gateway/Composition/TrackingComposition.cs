using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ParcelTrace.Gateway.Composition;

// downstream responses (only what we use)
public sealed record ShipmentSummary(string TrackingNumber, string Status, string ServiceLevel, string PickupCity, string DropoffCity,
    DateTimeOffset CreatedAt, DateTimeOffset SlaDueAt, DateTimeOffset? EtaAt, DateTimeOffset? DeliveredAt, string? DeliveredTo);

public sealed record TimelineEntry(string Status, string? Note, DateTimeOffset OccurredAt);

public sealed record Point(double Lat, double Lng);

public sealed record Destination(string City, string PostalCode, double Lat, double Lng);

public sealed record LastLocation(double Lat, double Lng, DateTimeOffset RecordedAt);

public sealed record TrackingEta(DateTimeOffset EtaAt, int? MinutesRemaining, double? DistanceKm, DateTimeOffset? CalculatedAt);

public sealed record TrackingView(string TrackingNumber, string Status, string ServiceLevel, string From, Destination To, string? CourierName,
    DateTimeOffset SlaDueAt, TrackingEta? Eta, LastLocation? LastLocation, IReadOnlyList<TimelineEntry> Timeline);

public sealed record LiveEstimate(double DistanceKm, int Minutes, DateTimeOffset EtaAt, double TrafficFactor);

public sealed record ComposedEta(DateTimeOffset EtaAt, int? MinutesRemaining, double? DistanceKm, string Source);

public sealed record SourceStatus(string Shipments, string Tracking, string Eta);

public sealed record PublicTracking(
    string TrackingNumber,
    string Status,
    string ServiceLevel,
    string From,
    Destination? To,
    string? CourierName,
    DateTimeOffset SlaDueAt,
    bool IsDelayed,
    ComposedEta? Eta,
    LastLocation? LastLocation,
    DateTimeOffset? DeliveredAt,
    string? DeliveredTo,
    IReadOnlyList<TimelineEntry> Timeline,
    SourceStatus Sources);

internal enum Fetch
{
    Ok,
    NotFound,
    Unavailable
}

public sealed partial class TrackingComposer(IHttpClientFactory clients, ILogger<TrackingComposer> logger, TimeProvider clock)
{
    public const string Shipments = "compose-shipments";
    public const string Tracking = "compose-tracking";
    public const string Eta = "compose-eta";

    private static readonly string[] OnTheWay = ["PickedUp", "InTransit", "OutForDelivery"];
    private static readonly string[] Finished = ["Delivered", "Failed", "Cancelled"];

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [GeneratedRegex("^PT-\\d{4}-[A-Z0-9]{6}$")]
    private static partial Regex TrackingNumberFormat();

    public async Task<IResult> ComposeAsync(string trackingNumber, CancellationToken ct)
    {
        var tn = trackingNumber.Trim().ToUpperInvariant();
        if (!TrackingNumberFormat().IsMatch(tn))
        {
            return Results.Problem(statusCode: 400, title: "Bad request", detail: "Tracking numbers look like PT-2024-ABC234.");
        }

        var shipmentTask = GetAsync<ShipmentSummary>(Shipments, $"/api/shipments/track/{tn}", ct);
        var trackingTask = GetAsync<TrackingView>(Tracking, $"/api/tracking/{tn}", ct);
        await Task.WhenAll(shipmentTask, trackingTask);
        var (shipmentState, shipment) = shipmentTask.Result;
        var (trackingState, tracking) = trackingTask.Result;

        if (shipmentState == Fetch.NotFound || (shipment is null && trackingState == Fetch.NotFound))
        {
            return Results.Problem(statusCode: 404, title: "Not found", detail: $"No shipment with tracking number {tn}.");
        }

        if (shipment is null && tracking is null)
        {
            return Results.Problem(statusCode: 503, title: "Dependency unavailable", detail: "Tracking is temporarily unavailable, please try again shortly.");
        }

        var status = tracking?.Status ?? shipment!.Status;
        var (eta, etaSource) = await EtaAsync(status, tracking, shipment, ct);
        var slaDueAt = shipment?.SlaDueAt ?? tracking!.SlaDueAt;
        var finished = Finished.Contains(status);

        return Results.Ok(new PublicTracking(
            TrackingNumber: tn,
            Status: status,
            ServiceLevel: shipment?.ServiceLevel ?? tracking!.ServiceLevel,
            From: shipment?.PickupCity ?? tracking!.From,
            To: tracking?.To,
            CourierName: tracking?.CourierName,
            SlaDueAt: slaDueAt,
            IsDelayed: !finished && (clock.GetUtcNow() > slaDueAt || (eta is not null && eta.EtaAt > slaDueAt)),
            Eta: finished ? null : eta,
            LastLocation: tracking?.LastLocation,
            DeliveredAt: shipment?.DeliveredAt,
            DeliveredTo: shipment?.DeliveredTo,
            Timeline: tracking?.Timeline ?? [],
            Sources: new SourceStatus(Describe(shipmentState), Describe(trackingState), etaSource)));
    }

    private async Task<(ComposedEta? Eta, string Source)> EtaAsync(string status, TrackingView? tracking, ShipmentSummary? shipment, CancellationToken ct)
    {
        if (tracking is { LastLocation: { } at, To: { } to } && OnTheWay.Contains(status))
        {
            var (state, live) = await PostAsync<LiveEstimate>(Eta, "/api/eta/estimate", new
            {
                origin = new Point(at.Lat, at.Lng),
                destination = new Point(to.Lat, to.Lng),
                serviceLevel = tracking.ServiceLevel
            }, ct);
            if (live is not null)
            {
                return (new ComposedEta(live.EtaAt, live.Minutes, live.DistanceKm, "live"), "ok");
            }

            return (Fallback(), Describe(state));
        }

        return (Fallback(), "skipped");

        ComposedEta? Fallback() =>
            tracking?.Eta is { } cached ? new ComposedEta(cached.EtaAt, cached.MinutesRemaining, cached.DistanceKm, "tracking")
            : shipment?.EtaAt is { } booked ? new ComposedEta(booked, null, null, "booking")
            : null;
    }

    private async Task<(Fetch, T?)> GetAsync<T>(string client, string path, CancellationToken ct) where T : class
    {
        try
        {
            using var response = await clients.CreateClient(client).GetAsync(path, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return (Fetch.NotFound, null);
            }

            response.EnsureSuccessStatusCode();
            return (Fetch.Ok, await response.Content.ReadFromJsonAsync<T>(Json, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Composition source {Client} unavailable", client);
            return (Fetch.Unavailable, null);
        }
    }

    private async Task<(Fetch, T?)> PostAsync<T>(string client, string path, object body, CancellationToken ct) where T : class
    {
        try
        {
            using var response = await clients.CreateClient(client).PostAsJsonAsync(path, body, Json, ct);
            response.EnsureSuccessStatusCode();
            return (Fetch.Ok, await response.Content.ReadFromJsonAsync<T>(Json, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Composition source {Client} unavailable", client);
            return (Fetch.Unavailable, null);
        }
    }

    private static string Describe(Fetch state) => state switch
    {
        Fetch.Ok => "ok",
        Fetch.NotFound => "not-found",
        _ => "unavailable"
    };
}

public static class CompositionExtensions
{
    public static IServiceCollection AddTrackingComposition(this IServiceCollection services, IConfiguration configuration)
    {
        void Client(string name, string cluster) =>
            services.AddHttpClient(name, c => c.BaseAddress = new Uri(configuration[$"ReverseProxy:Clusters:{cluster}:Destinations:d1:Address"]
                                                                     ?? throw new InvalidOperationException($"No address for cluster {cluster}")))
                .AddStandardResilienceHandler(o =>
                {
                    // public page, fail fast
                    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
                    o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                });

        Client(TrackingComposer.Shipments, "shipments");
        Client(TrackingComposer.Tracking, "tracking");
        Client(TrackingComposer.Eta, "eta");
        services.AddSingleton(TimeProvider.System);
        services.AddTransient<TrackingComposer>();
        return services;
    }

    public static IEndpointRouteBuilder MapTrackingComposition(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/track/{trackingNumber}", (string trackingNumber, TrackingComposer composer, CancellationToken ct) =>
                composer.ComposeAsync(trackingNumber, ct))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Public)
            .WithName("ComposePublicTracking")
            .WithTags("Public")
            .WithSummary("Public tracking composed from shipments, tracking and eta");
        return app;
    }
}
