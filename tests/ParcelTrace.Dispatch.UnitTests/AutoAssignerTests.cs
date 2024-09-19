using ParcelTrace.Contracts;
using ParcelTrace.Dispatch.Domain;

namespace ParcelTrace.Dispatch.UnitTests;

public class AutoAssignerTests
{
    private static readonly DateTime Now = new(2024, 3, 18, 10, 0, 0, DateTimeKind.Utc);
    private static readonly GeoPoint Alexanderplatz = new(52.5219, 13.4132);

    private static Courier Courier(string name, double lat, double lng, VehicleType vehicle = VehicleType.Car, int active = 0,
        int max = 8, double load = 0, double capacity = 150, bool available = true, DateTime? seen = null, Position? last = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            VehicleType = vehicle,
            HomeBase = Position.From(lat, lng),
            ActiveCount = active,
            MaxActiveShipments = max,
            CurrentLoadKg = load,
            CapacityKg = capacity,
            IsAvailable = available,
            LastSeenAt = seen,
            LastLocation = last
        };

    [Fact]
    public void Picks_the_nearest_available_courier()
    {
        var near = Courier("Near", 52.5200, 13.4100);
        var far = Courier("Far", 52.4500, 13.3000);

        var choice = AutoAssigner.PickBest(Alexanderplatz, 2, [far, near], Now);

        choice!.Courier.Should().BeSameAs(near);
        choice.DistanceKm.Should().BeLessThan(1);
    }

    [Fact]
    public void Skips_couriers_who_are_off_duty_full_or_overloaded()
    {
        var offDuty = Courier("Off", 52.5219, 13.4132, available: false);
        var full = Courier("Full", 52.5219, 13.4132, active: 8, max: 8);
        var heavy = Courier("Heavy", 52.5219, 13.4132, load: 145, capacity: 150);
        var ok = Courier("Ok", 52.50, 13.40);

        AutoAssigner.PickBest(Alexanderplatz, 10, [offDuty, full, heavy, ok], Now)!.Courier.Should().BeSameAs(ok);
    }

    [Fact]
    public void Returns_null_when_nobody_can_take_it()
    {
        var bike = Courier("Bike", 52.52, 13.41, VehicleType.Bike, capacity: 15);

        AutoAssigner.PickBest(Alexanderplatz, 20, [bike], Now).Should().BeNull();
    }

    [Fact]
    public void Uses_a_fresh_ping_over_the_home_base()
    {
        // far from home base, but pinged right at the pick-up 5 min ago
        var pinged = Courier("Pinged", 52.40, 13.20, seen: Now.AddMinutes(-5), last: Position.From(52.5218, 13.4130));
        var nearHome = Courier("NearHome", 52.5150, 13.4000);

        AutoAssigner.PickBest(Alexanderplatz, 1, [nearHome, pinged], Now)!.Courier.Should().BeSameAs(pinged);
    }

    [Fact]
    public void Ignores_stale_pings()
    {
        var stale = Courier("Stale", 52.40, 13.20, seen: Now.AddHours(-2), last: Position.From(52.5218, 13.4130));

        AutoAssigner.CurrentPosition(stale, Now).Should().Be(new GeoPoint(52.40, 13.20));
    }

    [Fact]
    public void Spreads_work_by_penalising_existing_load()
    {
        // busy one is closer but carries 3 parcels
        var busy = Courier("Busy", 52.5219, 13.4132, active: 3);
        var idle = Courier("Idle", 52.5219, 13.4060);

        AutoAssigner.PickBest(Alexanderplatz, 1, [busy, idle], Now)!.Courier.Should().BeSameAs(idle);
    }

    [Fact]
    public void Bikes_are_not_sent_across_town()
    {
        var bike = Courier("Bike", 52.4500, 13.3000, VehicleType.Bike, capacity: 15);
        var van = Courier("Van", 52.4400, 13.2900, VehicleType.Van, capacity: 400);

        AutoAssigner.PickBest(Alexanderplatz, 1, [bike, van], Now)!.Courier.Should().BeSameAs(van);
    }

    [Fact]
    public void Ranking_is_deterministic_on_ties()
    {
        var a = Courier("A", 52.52, 13.41);
        var b = Courier("B", 52.52, 13.41);

        var first = AutoAssigner.PickBest(Alexanderplatz, 1, [a, b], Now)!.Courier;
        var second = AutoAssigner.PickBest(Alexanderplatz, 1, [b, a], Now)!.Courier;

        first.Should().BeSameAs(second);
    }
}
