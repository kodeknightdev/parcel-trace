using Microsoft.Extensions.Options;
using MongoDB.Driver;
using ParcelTrace.Dispatch.Data;
using ParcelTrace.TestSupport;
using Testcontainers.MongoDb;

namespace ParcelTrace.Dispatch.IntegrationTests;

public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder().WithImage("mongo:7.0.8").Build();

    public async Task<DispatchDb> NewDatabaseAsync()
    {
        var db = new DispatchDb(new MongoClient(_mongo.GetConnectionString()),
            Options.Create(new MongoOptions { Database = $"dispatch_{Guid.NewGuid():N}" }));
        await db.EnsureIndexesAsync();
        return db;
    }

    public Task InitializeAsync() => DockerFactAttribute.IsDockerAvailable ? _mongo.StartAsync() : Task.CompletedTask;

    public Task DisposeAsync() => _mongo.DisposeAsync().AsTask();
}
