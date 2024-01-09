namespace ParcelTrace.Contracts;

public static class Roles
{
    public const string Customer = "Customer";
    public const string Dispatcher = "Dispatcher";
    public const string Courier = "Courier";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Customer, Dispatcher, Courier, Admin];
}

public static class ParcelClaims
{
    public const string Role = "role";
    public const string CourierId = "courier_id";
    public const string Name = "name";
}
