using MongoDB.Bson.Serialization.Attributes;
using ParcelTrace.Contracts;

namespace ParcelTrace.Dispatch.Domain;

public sealed class Position
{
    public double Lat { get; set; }
    public double Lng { get; set; }

    public GeoPoint ToPoint() => new(Lat, Lng);
    public static Position From(double lat, double lng) => new() { Lat = lat, Lng = lng };
}

public sealed class Courier
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public VehicleType VehicleType { get; set; }
    public double CapacityKg { get; set; }
    public int MaxActiveShipments { get; set; }

    public bool IsAvailable { get; set; }

    public List<Guid> ActiveShipmentIds { get; set; } = [];
    public int ActiveCount { get; set; }
    public double CurrentLoadKg { get; set; }

    public Position HomeBase { get; set; } = new();
    public Position? LastLocation { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum AssignmentStatus
{
    Active,
    Completed,
    Failed,
    Cancelled,
    Reassigned
}

public sealed class Stop
{
    public string Line1 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
}

public sealed class Assignment
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShipmentId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public Guid CourierId { get; set; }
    public string CourierName { get; set; } = string.Empty;
    public AssignmentMode Mode { get; set; }
    public string AssignedBy { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }

    public string RouteDate { get; set; } = string.Empty;

    public AssignmentStatus Status { get; set; } = AssignmentStatus.Active;
    public ShipmentStatus ShipmentStatus { get; set; } = ShipmentStatus.Assigned;
    public ServiceLevel ServiceLevel { get; set; }
    public double WeightKg { get; set; }
    public double DistanceKm { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public Stop Pickup { get; set; } = new();
    public Stop Dropoff { get; set; } = new();
    public DateTime SlaDueAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class PendingShipment
{
    [BsonId]
    public Guid ShipmentId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public ServiceLevel ServiceLevel { get; set; }
    public string PickupCity { get; set; } = string.Empty;
    public string PickupPostalCode { get; set; } = string.Empty;
    public string DropoffCity { get; set; } = string.Empty;
    public double PickupLat { get; set; }
    public double PickupLng { get; set; }
    public double WeightKg { get; set; }
    public DateTime SlaDueAt { get; set; }
    public DateTime QueuedAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }

    // copied from the saga for the queue view
    public string? OfferedTo { get; set; }
    public DateTime? OfferExpiresAt { get; set; }
    public bool Escalated { get; set; }
}

public sealed class ProcessedEvent
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
