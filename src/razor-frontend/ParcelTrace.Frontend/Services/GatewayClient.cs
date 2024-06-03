using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParcelTrace.Contracts;

namespace ParcelTrace.Frontend.Services;

public interface IGatewayClient
{
    // auth
    Task<ApiResult<AuthResponse>> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<ApiResult<AuthResponse>> RegisterAsync(string email, string password, string displayName, CancellationToken ct = default);

    // public
    Task<ApiResult<PublicTracking>> TrackAsync(string trackingNumber, CancellationToken ct = default);
    Task<ApiResult<Quote>> QuoteAsync(QuoteRequest request, CancellationToken ct = default);

    // customers
    Task<ApiResult<List<ShipmentDto>>> MyShipmentsAsync(CancellationToken ct = default);
    Task<ApiResult<ShipmentDto>> GetShipmentAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<ShipmentDto>> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken ct = default);
    Task<ApiResult<ShipmentDto>> CancelShipmentAsync(Guid id, string? reason, CancellationToken ct = default);

    // dispatchers
    Task<ApiResult<DailyCounts>> DailyCountsAsync(CancellationToken ct = default);
    Task<ApiResult<List<ShipmentDto>>> DelayedAsync(CancellationToken ct = default);
    Task<ApiResult<List<PendingShipment>>> QueueAsync(CancellationToken ct = default);
    Task<ApiResult<DispatchSummary>> DispatchSummaryAsync(CancellationToken ct = default);
    Task<ApiResult<List<CourierDto>>> CouriersAsync(CancellationToken ct = default);
    Task<ApiResult<CourierDto>> CreateCourierAsync(CourierRequest request, CancellationToken ct = default);
    Task<ApiResult<CourierDto>> SetCourierAvailabilityAsync(Guid courierId, bool available, CancellationToken ct = default);
    Task<ApiResult<AssignmentDto>> AssignAsync(Guid shipmentId, Guid courierId, CancellationToken ct = default);
    Task<ApiResult<OfferResult>> AutoAssignAsync(Guid shipmentId, CancellationToken ct = default);
    Task<ApiResult<List<OfferResult>>> AutoAssignAllAsync(CancellationToken ct = default);
    Task<ApiResult<List<AssignmentSaga>>> EscalationsAsync(CancellationToken ct = default);
    Task<ApiResult<DispatchBoard>> BoardAsync(CancellationToken ct = default);
    Task<ApiResult<RouteDto>> RouteAsync(Guid courierId, DateOnly date, CancellationToken ct = default);

    // couriers
    Task<ApiResult<CourierDto>> MyCourierAsync(CancellationToken ct = default);
    Task<ApiResult<RouteDto>> MyRouteAsync(DateOnly date, CancellationToken ct = default);
    Task<ApiResult<List<CourierOffer>>> MyOffersAsync(CancellationToken ct = default);
    Task<ApiResult<AssignmentDto>> AcceptOfferAsync(Guid shipmentId, CancellationToken ct = default);
    Task<ApiResult<OfferResult>> RejectOfferAsync(Guid shipmentId, string? reason, CancellationToken ct = default);
    Task<ApiResult<CourierDto>> SetMyAvailabilityAsync(bool available, CancellationToken ct = default);
    Task<ApiResult<StatusUpdateResult>> UpdateStatusAsync(Guid shipmentId, StatusUpdate update, CancellationToken ct = default);

    // admins
    Task<ApiResult<List<UserDto>>> UsersAsync(CancellationToken ct = default);
    Task<ApiResult<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
}

public sealed class GatewayClient(HttpClient http, ILogger<GatewayClient> logger) : IGatewayClient
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<ApiResult<AuthResponse>> LoginAsync(string email, string password, CancellationToken ct) =>
        SendAsync<AuthResponse>(HttpMethod.Post, "/api/auth/login", new { email, password }, ct);

    public Task<ApiResult<AuthResponse>> RegisterAsync(string email, string password, string displayName, CancellationToken ct) =>
        SendAsync<AuthResponse>(HttpMethod.Post, "/api/auth/register", new { email, password, displayName }, ct);

    public Task<ApiResult<PublicTracking>> TrackAsync(string trackingNumber, CancellationToken ct) =>
        SendAsync<PublicTracking>(HttpMethod.Get, $"/api/track/{Uri.EscapeDataString(trackingNumber.Trim().ToUpperInvariant())}", null, ct);

    public Task<ApiResult<Quote>> QuoteAsync(QuoteRequest request, CancellationToken ct) =>
        SendAsync<Quote>(HttpMethod.Post, "/api/shipments/quote", request, ct);

    public Task<ApiResult<List<ShipmentDto>>> MyShipmentsAsync(CancellationToken ct) =>
        SendAsync<List<ShipmentDto>>(HttpMethod.Get, "/api/shipments/mine", null, ct);

    public Task<ApiResult<ShipmentDto>> GetShipmentAsync(Guid id, CancellationToken ct) =>
        SendAsync<ShipmentDto>(HttpMethod.Get, $"/api/shipments/{id}", null, ct);

    public Task<ApiResult<ShipmentDto>> CreateShipmentAsync(CreateShipmentRequest request, CancellationToken ct) =>
        SendAsync<ShipmentDto>(HttpMethod.Post, "/api/shipments", request, ct);

    public Task<ApiResult<ShipmentDto>> CancelShipmentAsync(Guid id, string? reason, CancellationToken ct) =>
        SendAsync<ShipmentDto>(HttpMethod.Post, $"/api/shipments/{id}/cancel", new { reason }, ct);

    public Task<ApiResult<DailyCounts>> DailyCountsAsync(CancellationToken ct) =>
        SendAsync<DailyCounts>(HttpMethod.Get, "/api/shipments/dashboard/daily", null, ct);

    public Task<ApiResult<List<ShipmentDto>>> DelayedAsync(CancellationToken ct) =>
        SendAsync<List<ShipmentDto>>(HttpMethod.Get, "/api/shipments/dashboard/delayed", null, ct);

    public Task<ApiResult<List<PendingShipment>>> QueueAsync(CancellationToken ct) =>
        SendAsync<List<PendingShipment>>(HttpMethod.Get, "/api/dispatch/queue", null, ct);

    public Task<ApiResult<DispatchSummary>> DispatchSummaryAsync(CancellationToken ct) =>
        SendAsync<DispatchSummary>(HttpMethod.Get, "/api/dispatch/summary", null, ct);

    public Task<ApiResult<List<CourierDto>>> CouriersAsync(CancellationToken ct) =>
        SendAsync<List<CourierDto>>(HttpMethod.Get, "/api/couriers", null, ct);

    public Task<ApiResult<CourierDto>> CreateCourierAsync(CourierRequest request, CancellationToken ct) =>
        SendAsync<CourierDto>(HttpMethod.Post, "/api/couriers", request, ct);

    public Task<ApiResult<CourierDto>> SetCourierAvailabilityAsync(Guid courierId, bool available, CancellationToken ct) =>
        SendAsync<CourierDto>(HttpMethod.Put, $"/api/couriers/{courierId}/availability", new { isAvailable = available }, ct);

    public Task<ApiResult<AssignmentDto>> AssignAsync(Guid shipmentId, Guid courierId, CancellationToken ct) =>
        SendAsync<AssignmentDto>(HttpMethod.Post, "/api/dispatch/assign", new { shipmentId, courierId }, ct);

    public Task<ApiResult<OfferResult>> AutoAssignAsync(Guid shipmentId, CancellationToken ct) =>
        SendAsync<OfferResult>(HttpMethod.Post, "/api/dispatch/auto-assign", new { shipmentId }, ct);

    public Task<ApiResult<List<OfferResult>>> AutoAssignAllAsync(CancellationToken ct) =>
        SendAsync<List<OfferResult>>(HttpMethod.Post, "/api/dispatch/auto-assign/all", new { }, ct);

    public Task<ApiResult<List<AssignmentSaga>>> EscalationsAsync(CancellationToken ct) =>
        SendAsync<List<AssignmentSaga>>(HttpMethod.Get, "/api/dispatch/escalations", null, ct);

    public Task<ApiResult<DispatchBoard>> BoardAsync(CancellationToken ct) =>
        SendAsync<DispatchBoard>(HttpMethod.Get, "/api/tracking/board", null, ct);

    public Task<ApiResult<RouteDto>> RouteAsync(Guid courierId, DateOnly date, CancellationToken ct) =>
        SendAsync<RouteDto>(HttpMethod.Get, $"/api/dispatch/routes/{courierId}?date={date:yyyy-MM-dd}", null, ct);

    public Task<ApiResult<CourierDto>> MyCourierAsync(CancellationToken ct) =>
        SendAsync<CourierDto>(HttpMethod.Get, "/api/couriers/me", null, ct);

    public Task<ApiResult<RouteDto>> MyRouteAsync(DateOnly date, CancellationToken ct) =>
        SendAsync<RouteDto>(HttpMethod.Get, $"/api/dispatch/routes/me?date={date:yyyy-MM-dd}", null, ct);

    public Task<ApiResult<List<CourierOffer>>> MyOffersAsync(CancellationToken ct) =>
        SendAsync<List<CourierOffer>>(HttpMethod.Get, "/api/dispatch/offers/me", null, ct);

    public Task<ApiResult<AssignmentDto>> AcceptOfferAsync(Guid shipmentId, CancellationToken ct) =>
        SendAsync<AssignmentDto>(HttpMethod.Post, $"/api/dispatch/offers/{shipmentId}/accept", new { }, ct);

    public Task<ApiResult<OfferResult>> RejectOfferAsync(Guid shipmentId, string? reason, CancellationToken ct) =>
        SendAsync<OfferResult>(HttpMethod.Post, $"/api/dispatch/offers/{shipmentId}/reject", new { reason }, ct);

    public Task<ApiResult<CourierDto>> SetMyAvailabilityAsync(bool available, CancellationToken ct) =>
        SendAsync<CourierDto>(HttpMethod.Put, "/api/couriers/me/availability", new { isAvailable = available }, ct);

    public Task<ApiResult<StatusUpdateResult>> UpdateStatusAsync(Guid shipmentId, StatusUpdate update, CancellationToken ct) =>
        SendAsync<StatusUpdateResult>(HttpMethod.Post, $"/api/tracking/shipments/{shipmentId}/status", update, ct);

    public Task<ApiResult<List<UserDto>>> UsersAsync(CancellationToken ct) =>
        SendAsync<List<UserDto>>(HttpMethod.Get, "/api/users", null, ct);

    public Task<ApiResult<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken ct) =>
        SendAsync<UserDto>(HttpMethod.Post, "/api/users", request, ct);

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException)
        {
            logger.LogWarning(ex, "Gateway call {Method} {Path} failed", method, path);
            return ApiResult<T>.Failure(503, "The service is temporarily unavailable. Please try again in a moment.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);
                return ApiResult<T>.Success(value!, (int)response.StatusCode);
            }

            return await ToFailureAsync<T>(response, ct);
        }
    }

    private static async Task<ApiResult<T>> ToFailureAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Json, ct);
            if (problem is not null)
            {
                var message = problem.Errors is { Count: > 0 }
                    ? string.Join(" ", problem.Errors.SelectMany(e => e.Value))
                    : problem.Detail ?? problem.Title ?? response.ReasonPhrase ?? "Request failed";
                return ApiResult<T>.Failure(status, message, problem.Errors);
            }
        }
        catch (JsonException)
        {
            // not problem+json
        }

        return ApiResult<T>.Failure(status, response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired, please sign in again.",
            HttpStatusCode.Forbidden => "You are not allowed to do that.",
            HttpStatusCode.NotFound => "Not found.",
            HttpStatusCode.TooManyRequests => "Too many requests, slow down a little.",
            _ => response.ReasonPhrase ?? "Request failed"
        });
    }

    private sealed record ProblemBody(string? Title, string? Detail, int? Status, Dictionary<string, string[]>? Errors);
}
