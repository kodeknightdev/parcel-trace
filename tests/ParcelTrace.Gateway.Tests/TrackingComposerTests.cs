using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ParcelTrace.Gateway.Composition;

namespace ParcelTrace.Gateway.Tests;

public class TrackingComposerTests
{
    private const string Tn = "PT-2024-ABC234";
    private static readonly DateTimeOffset Now = new(2024, 5, 29, 10, 0, 0, TimeSpan.Zero);

    private const string Shipment = """
        {"trackingNumber":"PT-2024-ABC234","status":"InTransit","serviceLevel":"Express","pickupCity":"Berlin","dropoffCity":"Berlin",
         "createdAt":"2024-05-29T08:00:00Z","slaDueAt":"2024-05-29T16:00:00Z","etaAt":"2024-05-29T11:00:00Z","deliveredAt":null,"deliveredTo":null}
        """;

    private const string Tracking = """
        {"trackingNumber":"PT-2024-ABC234","status":"InTransit","serviceLevel":"Express","from":"Berlin",
         "to":{"city":"Berlin","postalCode":"10719","lat":52.5,"lng":13.33},"courierName":"Lena","slaDueAt":"2024-05-29T16:00:00Z",
         "eta":{"etaAt":"2024-05-29T10:40:00Z","minutesRemaining":40,"distanceKm":9.1,"calculatedAt":"2024-05-29T09:58:00Z"},
         "lastLocation":{"lat":52.52,"lng":13.41,"recordedAt":"2024-05-29T09:59:00Z"},
         "timeline":[{"status":"Created","note":"Booked Express","occurredAt":"2024-05-29T08:00:00Z"},{"status":"InTransit","note":null,"occurredAt":"2024-05-29T09:30:00Z"}]}
        """;

    private const string Estimate = """{"distanceKm":7.8,"minutes":24,"etaAt":"2024-05-29T10:24:00Z","trafficFactor":1.15}""";

    [Fact]
    public async Task Merges_shipments_tracking_and_a_live_eta()
    {
        var (status, body) = await ComposeAsync(Ok(Shipment), Ok(Tracking), Ok(Estimate));

        status.Should().Be(200);
        body.GetProperty("status").GetString().Should().Be("InTransit");
        body.GetProperty("timeline").GetArrayLength().Should().Be(2);
        body.GetProperty("eta").GetProperty("source").GetString().Should().Be("live");
        body.GetProperty("eta").GetProperty("minutesRemaining").GetInt32().Should().Be(24);
        body.GetProperty("sources").GetProperty("eta").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task Falls_back_to_the_stored_eta_when_eta_is_down()
    {
        var (status, body) = await ComposeAsync(Ok(Shipment), Ok(Tracking), Fail());

        status.Should().Be(200);
        body.GetProperty("eta").GetProperty("source").GetString().Should().Be("tracking");
        body.GetProperty("sources").GetProperty("eta").GetString().Should().Be("unavailable");
    }

    [Fact]
    public async Task Still_answers_from_shipments_when_tracking_is_down()
    {
        var (status, body) = await ComposeAsync(Ok(Shipment), Fail(), Ok(Estimate));

        status.Should().Be(200);
        body.GetProperty("status").GetString().Should().Be("InTransit");
        body.GetProperty("timeline").GetArrayLength().Should().Be(0);
        body.GetProperty("eta").GetProperty("source").GetString().Should().Be("booking");
        body.GetProperty("sources").GetProperty("tracking").GetString().Should().Be("unavailable");
    }

    [Fact]
    public async Task Unknown_tracking_numbers_are_404_and_malformed_ones_400()
    {
        (await ComposeAsync(NotFound(), NotFound(), Fail())).Status.Should().Be(404);
        (await ComposeAsync(Ok(Shipment), Ok(Tracking), Ok(Estimate), trackingNumber: "nonsense")).Status.Should().Be(400);
    }

    [Fact]
    public async Task Everything_down_is_503()
    {
        (await ComposeAsync(Fail(), Fail(), Fail())).Status.Should().Be(503);
    }

    private static Func<HttpResponseMessage> Ok(string json) => () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static Func<HttpResponseMessage> NotFound() => () => new HttpResponseMessage(HttpStatusCode.NotFound);
    private static Func<HttpResponseMessage> Fail() => () => throw new HttpRequestException("connection refused");

    private static async Task<(int Status, JsonElement Body)> ComposeAsync(
        Func<HttpResponseMessage> shipments, Func<HttpResponseMessage> tracking, Func<HttpResponseMessage> eta, string trackingNumber = Tn)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHttpClient(TrackingComposer.Shipments, c => c.BaseAddress = new Uri("http://shipments")).ConfigurePrimaryHttpMessageHandler(() => new Stub(shipments));
        services.AddHttpClient(TrackingComposer.Tracking, c => c.BaseAddress = new Uri("http://tracking")).ConfigurePrimaryHttpMessageHandler(() => new Stub(tracking));
        services.AddHttpClient(TrackingComposer.Eta, c => c.BaseAddress = new Uri("http://eta")).ConfigurePrimaryHttpMessageHandler(() => new Stub(eta));
        var provider = services.BuildServiceProvider();

        var composer = new TrackingComposer(provider.GetRequiredService<IHttpClientFactory>(), NullLogger<TrackingComposer>.Instance, new FakeTimeProvider(Now));
        var result = await composer.ComposeAsync(trackingNumber, default);

        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.Body = new MemoryStream();
        await result.ExecuteAsync(http);
        http.Response.Body.Position = 0;
        var text = await new StreamReader(http.Response.Body).ReadToEndAsync();
        return (http.Response.StatusCode, string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement);
    }

    private sealed class Stub(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond());
    }
}
