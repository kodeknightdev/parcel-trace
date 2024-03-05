using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using ParcelTrace.Dispatch.Domain;

namespace ParcelTrace.Dispatch.Data;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string Database { get; set; } = "dispatch";
}

public sealed class DispatchDb
{
    private static int _conventionsRegistered;

    public DispatchDb(IMongoClient client, IOptions<MongoOptions> options)
    {
        RegisterConventions();
        Database = client.GetDatabase(options.Value.Database);
        Couriers = Database.GetCollection<Courier>("couriers");
        Assignments = Database.GetCollection<Assignment>("assignments");
        Pending = Database.GetCollection<PendingShipment>("pending_shipments");
        Processed = Database.GetCollection<ProcessedEvent>("processed_events");
    }

    public IMongoDatabase Database { get; }
    public IMongoCollection<Courier> Couriers { get; }
    public IMongoCollection<Assignment> Assignments { get; }
    public IMongoCollection<PendingShipment> Pending { get; }
    public IMongoCollection<ProcessedEvent> Processed { get; }

    public static void RegisterConventions()
    {
        if (Interlocked.Exchange(ref _conventionsRegistered, 1) == 1)
        {
            return;
        }

        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        ConventionRegistry.Register("parcel-trace", new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreExtraElementsConvention(true)
        }, t => t.Namespace?.StartsWith("ParcelTrace", StringComparison.Ordinal) == true);
    }

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        await Couriers.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<Courier>(Builders<Courier>.IndexKeys.Ascending(c => c.IsAvailable).Ascending(c => c.ActiveCount),
                new CreateIndexOptions { Name = "ix_available_load" }),
            new CreateIndexModel<Courier>(Builders<Courier>.IndexKeys.Ascending(c => c.Name),
                new CreateIndexOptions { Name = "ix_name" })
        ], ct);

        await Assignments.Indexes.CreateManyAsync(
        [
            // one active assignment per shipment
            new CreateIndexModel<Assignment>(Builders<Assignment>.IndexKeys.Ascending(a => a.ShipmentId),
                new CreateIndexOptions<Assignment>
                {
                    Name = "ux_active_shipment",
                    Unique = true,
                    PartialFilterExpression = Builders<Assignment>.Filter.Eq(a => a.Status, AssignmentStatus.Active)
                }),
            new CreateIndexModel<Assignment>(Builders<Assignment>.IndexKeys.Ascending(a => a.CourierId).Ascending(a => a.RouteDate),
                new CreateIndexOptions { Name = "ix_courier_route" }),
            new CreateIndexModel<Assignment>(Builders<Assignment>.IndexKeys.Ascending(a => a.ShipmentId).Descending(a => a.AssignedAt),
                new CreateIndexOptions { Name = "ix_shipment_history" })
        ], ct);

        await Pending.Indexes.CreateOneAsync(
            new CreateIndexModel<PendingShipment>(Builders<PendingShipment>.IndexKeys.Ascending(p => p.QueuedAt),
                new CreateIndexOptions { Name = "ix_queued_at" }), cancellationToken: ct);

        await Processed.Indexes.CreateOneAsync(
            new CreateIndexModel<ProcessedEvent>(Builders<ProcessedEvent>.IndexKeys.Ascending(p => p.ProcessedAt),
                new CreateIndexOptions { Name = "ttl_processed_at", ExpireAfter = TimeSpan.FromDays(7) }), cancellationToken: ct);
    }
}
