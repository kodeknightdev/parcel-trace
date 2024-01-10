namespace ParcelTrace.Contracts;

public enum ShipmentStatus
{
    Created,
    Assigned,
    PickedUp,
    InTransit,
    OutForDelivery,
    Delivered,
    Failed,
    Cancelled
}

public enum ServiceLevel
{
    Standard,
    Express
}

public enum VehicleType
{
    Bike,
    Scooter,
    Car,
    Van
}

public enum AssignmentMode
{
    Manual,
    Auto
}
