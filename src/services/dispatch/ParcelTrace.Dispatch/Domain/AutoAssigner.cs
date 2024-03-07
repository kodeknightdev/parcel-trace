using ParcelTrace.Contracts;

namespace ParcelTrace.Dispatch.Domain;

public sealed record AssignmentChoice(Courier Courier, double DistanceKm, double Score);

public static class AutoAssigner
{
    public const double LoadPenaltyKm = 1.5;
    public static readonly TimeSpan LocationFreshFor = TimeSpan.FromMinutes(30);

    public static double MaxPickupDistanceKm(VehicleType vehicle) => vehicle switch
    {
        VehicleType.Bike => 6,
        VehicleType.Scooter => 15,
        _ => 60
    };

    public static bool CanCarry(Courier courier, double weightKg) =>
        courier.IsAvailable
        && courier.ActiveCount < courier.MaxActiveShipments
        && courier.CurrentLoadKg + weightKg <= courier.CapacityKg;

    public static GeoPoint CurrentPosition(Courier courier, DateTime nowUtc) =>
        courier.LastLocation is { } last && courier.LastSeenAt is { } seen && nowUtc - seen <= LocationFreshFor
            ? last.ToPoint()
            : courier.HomeBase.ToPoint();

    public static AssignmentChoice? PickBest(GeoPoint pickup, double weightKg, IEnumerable<Courier> couriers, DateTime nowUtc) =>
        Rank(pickup, weightKg, couriers, nowUtc).FirstOrDefault();

    public static IEnumerable<AssignmentChoice> Rank(GeoPoint pickup, double weightKg, IEnumerable<Courier> couriers, DateTime nowUtc) =>
        couriers
            .Where(c => CanCarry(c, weightKg))
            .Select(c =>
            {
                var distance = Geo.HaversineKm(CurrentPosition(c, nowUtc), pickup);
                return new AssignmentChoice(c, Math.Round(distance, 2), distance + c.ActiveCount * LoadPenaltyKm);
            })
            .Where(choice => choice.DistanceKm <= MaxPickupDistanceKm(choice.Courier.VehicleType))
            .OrderBy(choice => choice.Score)
            .ThenBy(choice => choice.Courier.ActiveCount)
            .ThenBy(choice => choice.Courier.Id);
}
