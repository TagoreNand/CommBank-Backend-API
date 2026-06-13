using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MongoDB.Driver;

namespace CommBank.Tests.Integration;

/// <summary>
/// Starts a single-node MongoDB <b>replica set</b> in a container (required for multi-document
/// transactions), initialises it, waits for a primary, and exposes a <c>directConnection</c> client.
/// Shared across the "Mongo" collection so the container starts once. Requires Docker on the host/runner.
/// </summary>
public sealed class MongoReplicaSetFixture : IAsyncLifetime
{
    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("mongo:6.0")
        .WithCommand("--replSet", "rs0", "--bind_ip_all")
        .WithPortBinding(27017, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(27017))
        .Build();

    public IMongoClient Client { get; private set; } = default!;

    public IMongoDatabase Database { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Initialise the replica set (idempotent — swallow "already initialised").
        await _container.ExecAsync(new List<string>
        {
            "mongosh", "--quiet", "--eval",
            "try { rs.initiate({ _id: 'rs0', members: [{ _id: 0, host: 'localhost:27017' }] }) } catch (e) { }"
        });

        // Wait until a writable primary has been elected before handing out a client.
        for (int attempt = 0; attempt < 40; attempt++)
        {
            ExecResult check = await _container.ExecAsync(new List<string>
            {
                "mongosh", "--quiet", "--eval",
                "try { quit(db.hello().isWritablePrimary ? 0 : 1) } catch (e) { quit(1) }"
            });

            if (check.ExitCode == 0)
            {
                break;
            }

            await Task.Delay(500);
        }

        string connectionString =
            $"mongodb://{_container.Hostname}:{_container.GetMappedPublicPort(27017)}/?replicaSet=rs0&directConnection=true";

        Client = new MongoClient(connectionString);
        Database = Client.GetDatabase("CommBankIntegration");
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>xUnit collection so the container is shared across all integration tests.</summary>
[CollectionDefinition("Mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoReplicaSetFixture>
{
}
