using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NSubstitute;
using ParcelTrace.Contracts;
using ParcelTrace.Frontend.Pages;
using ParcelTrace.Frontend.Pages.Account;
using ParcelTrace.Frontend.Pages.Courier;
using ParcelTrace.Frontend.Pages.Shipments;
using ParcelTrace.Frontend.Services;

namespace ParcelTrace.Frontend.Tests;

public class PageModelTests
{
    private readonly IGatewayClient _gateway = Substitute.For<IGatewayClient>();

    private static T WithContext<T>(T model) where T : PageModel
    {
        var http = new DefaultHttpContext();
        model.PageContext = new PageContext { HttpContext = http };
        model.TempData = new TempDataDictionary(http, Substitute.For<ITempDataProvider>());
        return model;
    }

    private static PublicTracking Tracking(ShipmentStatus status, string etaSource = "ok") => new(
        "PT-2024-ABC234", status, ServiceLevel.Express, "Berlin", new DestinationView("Berlin", "10719", 52.5, 13.33), "Lena",
        DateTimeOffset.UtcNow.AddHours(4), false, null, null, null, null,
        [new TimelineEntry(ShipmentStatus.Created, "Booked Express", DateTimeOffset.UtcNow)],
        new SourceStatus("ok", "ok", etaSource));

    [Fact]
    public async Task Track_shows_the_parcel_when_found()
    {
        _gateway.TrackAsync("pt-2024-abc234", Arg.Any<CancellationToken>()).Returns(ApiResult<PublicTracking>.Success(Tracking(ShipmentStatus.InTransit)));
        var page = WithContext(new TrackModel(_gateway) { Tn = "pt-2024-abc234" });

        await page.OnGetAsync(default);

        page.Tracking!.Status.Should().Be(ShipmentStatus.InTransit);
        page.Error.Should().BeNull();
        page.Missing.Should().BeEmpty();
    }

    [Fact]
    public async Task Track_says_which_composed_parts_are_missing()
    {
        _gateway.TrackAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<PublicTracking>.Success(Tracking(ShipmentStatus.InTransit, etaSource: "unavailable")));
        var page = WithContext(new TrackModel(_gateway) { Tn = "PT-2024-ABC234" });

        await page.OnGetAsync(default);

        page.Missing.Should().Equal("live ETA");
    }

    [Fact]
    public async Task Track_explains_an_unknown_number()
    {
        _gateway.TrackAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<PublicTracking>.Failure(404, "Not found"));
        var page = WithContext(new TrackModel(_gateway) { Tn = "PT-2024-ZZZZZZ" });

        await page.OnGetAsync(default);

        page.Tracking.Should().BeNull();
        page.Error.Should().Contain("PT-2024-ZZZZZZ");
    }

    [Fact]
    public async Task Track_without_a_number_does_not_call_the_gateway()
    {
        var page = WithContext(new TrackModel(_gateway));

        await page.OnGetAsync(default);

        await _gateway.DidNotReceiveWithAnyArgs().TrackAsync(default!, default);
    }

    [Fact]
    public async Task Login_with_wrong_password_stays_on_the_page()
    {
        _gateway.LoginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult<AuthResponse>.Failure(401, "Invalid credentials"));
        var signIn = Substitute.For<ICookieSignIn>();
        var page = WithContext(new LoginModel(_gateway, signIn) { Input = new() { Email = "customer@parceltrace.local", Password = "nope" } });

        var result = await page.OnPostAsync(default);

        result.Should().BeOfType<PageResult>();
        page.ModelState.ErrorCount.Should().Be(1);
        await signIn.DidNotReceiveWithAnyArgs().SignInAsync(default!, default!);
    }

    [Theory]
    [InlineData(Roles.Customer, "/Shipments/Index")]
    [InlineData(Roles.Dispatcher, "/Dispatch/Index")]
    [InlineData(Roles.Courier, "/Courier/Index")]
    public async Task Login_signs_in_and_sends_each_role_to_its_home(string role, string expectedPage)
    {
        var auth = new AuthResponse("token", DateTimeOffset.UtcNow.AddHours(2), new UserDto(Guid.NewGuid(), "x@y.z", "X", role, null, true, DateTimeOffset.UtcNow));
        _gateway.LoginAsync("x@y.z", "Demo@12345", Arg.Any<CancellationToken>()).Returns(ApiResult<AuthResponse>.Success(auth));
        var signIn = Substitute.For<ICookieSignIn>();
        var page = WithContext(new LoginModel(_gateway, signIn) { Input = new() { Email = "x@y.z", Password = "Demo@12345" } });

        var result = await page.OnPostAsync(default);

        result.Should().BeOfType<RedirectToPageResult>().Which.PageName.Should().Be(expectedPage);
        await signIn.Received(1).SignInAsync(Arg.Any<HttpContext>(), auth);
    }

    [Fact]
    public async Task Booking_redirects_to_the_new_shipment()
    {
        var created = SampleShipment();
        _gateway.CreateShipmentAsync(Arg.Any<CreateShipmentRequest>(), Arg.Any<CancellationToken>()).Returns(ApiResult<ShipmentDto>.Success(created, 201));
        var page = WithContext(new CreateModel(_gateway) { Input = ValidInput() });

        var result = await page.OnPostAsync(default);

        result.Should().BeOfType<RedirectToPageResult>().Which.RouteValues!["id"].Should().Be(created.Id);
        page.TempData["Flash"].Should().Be($"Booked! Your tracking number is {created.TrackingNumber}.");
    }

    [Fact]
    public async Task Booking_shows_api_validation_errors()
    {
        _gateway.CreateShipmentAsync(Arg.Any<CreateShipmentRequest>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<ShipmentDto>.Failure(400, "invalid", new Dictionary<string, string[]> { ["Dropoff"] = ["Too far away."] }));
        var page = WithContext(new CreateModel(_gateway) { Input = ValidInput() });

        var result = await page.OnPostAsync(default);

        result.Should().BeOfType<PageResult>();
        page.ModelState[string.Empty]!.Errors.Should().ContainSingle(e => e.ErrorMessage == "Dropoff: Too far away.");
    }

    [Fact]
    public void Booking_input_maps_to_the_api_request()
    {
        var request = ValidInput().ToRequest();

        request.ServiceLevel.Should().Be(ServiceLevel.Express);
        request.Pickup.Country.Should().Be("DE");
        request.Notes.Should().BeNull();
        request.Package.WeightKg.Should().Be(2.5m);
    }

    [Fact]
    public async Task Delivering_without_a_signature_is_refused_before_calling_the_api()
    {
        var page = WithContext(new DeliverModel(_gateway) { Input = new() { RecipientName = "Jonas" } });

        var result = await page.OnPostAsync(Guid.NewGuid(), default);

        result.Should().BeOfType<PageResult>();
        await _gateway.DidNotReceiveWithAnyArgs().UpdateStatusAsync(default, default!, default);
    }

    [Fact]
    public async Task Delivering_sends_the_proof()
    {
        var id = Guid.NewGuid();
        _gateway.UpdateStatusAsync(id, Arg.Any<StatusUpdate>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult<StatusUpdateResult>.Success(new StatusUpdateResult(id, "PT-2024-ABC234", ShipmentStatus.Delivered, true)));
        var page = WithContext(new DeliverModel(_gateway) { Input = new() { RecipientName = " Jonas Weber ", SignatureName = "J. Weber" } });

        var result = await page.OnPostAsync(id, default);

        result.Should().BeOfType<RedirectToPageResult>();
        await _gateway.Received(1).UpdateStatusAsync(id,
            Arg.Is<StatusUpdate>(u => u.Status == "Delivered" && u.Proof!.RecipientName == "Jonas Weber" && u.Proof.PhotoUrl == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Couriers_accept_offers_from_their_route_page()
    {
        var shipmentId = Guid.NewGuid();
        _gateway.AcceptOfferAsync(shipmentId, Arg.Any<CancellationToken>()).Returns(ApiResult<AssignmentDto>.Success(new AssignmentDto(
            Guid.NewGuid(), shipmentId, "PT-2024-ABC234", Guid.NewGuid(), "Lena Fischer", AssignmentMode.Auto, "accepted by Lena Fischer",
            DateTime.UtcNow, "2024-06-18", "Active", ShipmentStatus.Assigned, ServiceLevel.Express, 1.5, 1.2, "Jonas Weber",
            new StopDto("A", "Berlin", "10178", 52.5, 13.4), new StopDto("B", "Berlin", "10719", 52.5, 13.3), DateTime.UtcNow.AddHours(8), null)));
        var page = WithContext(new ParcelTrace.Frontend.Pages.Courier.IndexModel(_gateway, TimeProvider.System));

        var result = await page.OnPostAcceptAsync(shipmentId, default);

        result.Should().BeOfType<RedirectToPageResult>();
        page.TempData["Flash"].Should().Be("PT-2024-ABC234 is on your route.");
    }

    [Fact]
    public void Couriers_only_get_buttons_for_the_next_step()
    {
        Display.CourierNextSteps(ShipmentStatus.Assigned).Should().Equal(ShipmentStatus.PickedUp);
        Display.CourierNextSteps(ShipmentStatus.OutForDelivery).Should().Equal(ShipmentStatus.Delivered);
        Display.CourierNextSteps(ShipmentStatus.Delivered).Should().BeEmpty();
    }

    [Fact]
    public void Cookie_principal_keeps_role_courier_id_and_token()
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var courierId = Guid.NewGuid();
        var token = handler.WriteToken(new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(claims: [new(ParcelClaims.CourierId, courierId.ToString())]));
        var auth = new AuthResponse(token, DateTimeOffset.UtcNow.AddHours(1), new UserDto(Guid.NewGuid(), "courier2@parceltrace.local", "Lena", Roles.Courier, courierId, true, DateTimeOffset.UtcNow));

        var principal = CookieSignIn.CreatePrincipal(auth);

        principal.IsInRole(Roles.Courier).Should().BeTrue();
        principal.Identity!.Name.Should().Be("Lena");
        principal.FindFirst(ParcelClaims.CourierId)!.Value.Should().Be(courierId.ToString());
        principal.FindFirst(CookieClaims.AccessToken)!.Value.Should().Be(token);
    }

    private static CreateModel.ShipmentInput ValidInput() => new()
    {
        SenderName = "Alice", SenderPhone = "+49 30 1234567", PickupLine1 = "Alexanderplatz 1", PickupPostalCode = "10178",
        RecipientName = "Jonas", RecipientPhone = "+49 151 2345678", DropoffLine1 = "Kurfürstendamm 21", DropoffPostalCode = "10719",
        WeightKg = 2.5m, ServiceLevel = ServiceLevel.Express, Notes = " "
    };

    private static ShipmentDto SampleShipment() => new(
        Guid.NewGuid(), "PT-2024-ABC234", ShipmentStatus.Created, ServiceLevel.Express, Guid.NewGuid(),
        new PartyDto("Alice", "+49"), new PartyDto("Jonas", "+49"),
        new AddressDto("A", "Berlin", "10178", "DE", 52.5, 13.4), new AddressDto("B", "Berlin", "10719", "DE", 52.5, 13.3),
        new PackageDto(2.5m, 30, 20, 10), null, 14.2m, "EUR", 7.4, null, null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30), true, DateTimeOffset.UtcNow.AddHours(8), null, false,
        null, null, null, null, null, null, null);
}
