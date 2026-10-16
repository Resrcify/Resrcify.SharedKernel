using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Xunit;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests.Fixtures;

/// <summary>One Postgres and one RabbitMQ container for the whole run. Tests isolate by database and queue name.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit class fixtures must be public for cross-assembly fixture discovery.")]
public sealed class BusFixture : IAsyncLifetime
{
    private const string Username = "integration";
    private const string Password = "integration";

    public PostgresContainerFixture Postgres { get; } = new()
    {
        Database = "integration",
        Username = Username,
        Password = Password,
    };

    public RabbitMqContainerFixture RabbitMq { get; } = new()
    {
        Username = Username,
        Password = Password,
    };

    public RabbitMqConnection RabbitMqConnection
        => RabbitMq.Connection;

    /// <summary>
    /// A virtual host of its own whose queues are quorum queues unless declared otherwise, as on a production cluster.
    /// </summary>
    public Task<RabbitMqConnection> QuorumVirtualHostAsync()
        => RabbitMq.AddQuorumVirtualHostAsync();

    /// <summary>The type of <paramref name="queue"/> (classic, quorum) in <paramref name="virtualHost"/>.</summary>
    public async Task<string?> QueueTypeAsync(string virtualHost, string queue)
        => (await RabbitMq.QueuesAsync(virtualHost)).FirstOrDefault(listed => listed.Name == queue)?.Type;

    public async Task InitializeAsync()
    {
        await Postgres.InitializeAsync();
        await RabbitMq.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await RabbitMq.DisposeAsync();
        await Postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit collection definitions must be public.")]
public sealed class BusCollection : ICollectionFixture<BusFixture>
{
    public const string Name = nameof(BusCollection);
}
