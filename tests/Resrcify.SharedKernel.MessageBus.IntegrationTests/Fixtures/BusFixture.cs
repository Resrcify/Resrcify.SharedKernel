using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
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

    public PostgresFixture Postgres { get; } = new();
    public RabbitMqFixture RabbitMq { get; } = new();

    public RabbitMqConnection RabbitMqConnection
        => new(RabbitMq.Host, int.Parse(RabbitMq.AmqpPort, CultureInfo.InvariantCulture), Username, Password);

    /// <summary>
    /// A virtual host of its own whose queues are quorum queues unless declared otherwise, as on a production cluster.
    /// </summary>
    public async Task<RabbitMqConnection> QuorumVirtualHostAsync()
    {
        var virtualHost = $"quorum-{Guid.NewGuid():N}";
        await RabbitMqCtlAsync("add_vhost", virtualHost, "--default-queue-type", "quorum");
        await RabbitMqCtlAsync("set_permissions", "-p", virtualHost, Username, ".*", ".*", ".*");
        return RabbitMqConnection with { VirtualHost = virtualHost };
    }

    /// <summary>The type of <paramref name="queue"/> (classic, quorum) in <paramref name="virtualHost"/>.</summary>
    public async Task<string?> QueueTypeAsync(string virtualHost, string queue)
    {
        var listed = await RabbitMqCtlAsync("list_queues", "-p", virtualHost, "--quiet", "--no-table-headers", "name", "type");
        return listed
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t'))
            .FirstOrDefault(columns => columns[0] == queue)?[1];
    }

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

    private async Task<string> RabbitMqCtlAsync(params string[] arguments)
    {
        var result = await RabbitMq.Container.ExecAsync(["rabbitmqctl", .. arguments]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"rabbitmqctl {string.Join(' ', arguments)} failed: {result.Stderr}");
        return result.Stdout;
    }

    [SuppressMessage(
        "Performance",
        "CA1515:Consider making public types internal",
        Justification = "Exposed through the public fixture.")]
    public sealed class PostgresFixture : PostgresContainerFixture
    {
        protected override PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
            => builder
                .WithDatabase("integration")
                .WithUsername(Username)
                .WithPassword(Password);
    }

    [SuppressMessage(
        "Performance",
        "CA1515:Consider making public types internal",
        Justification = "Exposed through the public fixture.")]
    public sealed class RabbitMqFixture : RabbitMqContainerFixture
    {
        protected override RabbitMqBuilder Configure(RabbitMqBuilder builder)
            => builder
                .WithUsername(Username)
                .WithPassword(Password);
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
