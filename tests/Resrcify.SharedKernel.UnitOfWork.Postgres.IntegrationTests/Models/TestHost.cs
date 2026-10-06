using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Fixtures;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;

/// <summary>
/// A host set up as a service sets itself up: the context through <c>AddPostgresDbContext</c> on a fresh database, the
/// mediator, the outbox job and lanes on Quartz, with polling intervals long enough that only a wake-up can explain a
/// prompt delivery.
/// </summary>
internal sealed class TestHost : IAsyncDisposable
{
    /// <summary>How long the outbox job and the lanes wait between polls: far longer than any test runs.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

    private TestHost(IHost host)
        => Host = host;

    public IHost Host { get; }

    public IServiceProvider Services => Host.Services;

    public EventTracker Tracker => Services.GetRequiredService<EventTracker>();

    public static async Task<TestHost> CreateAsync(
        PostgresFixture postgres,
        Action<PostgresDbContextBuilder> configureDb,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(postgres.IsolatedDatabaseSettings());
        builder.Services.AddLogging();
        builder.Services.AddSingleton<EventTracker>();
        builder.Services.AddMediator(cfg => cfg.RegisterServicesFromAssemblies(typeof(TestHost).Assembly));
        builder.Services.AddPostgresDbContext<TestDbContext>(builder.Configuration, configureDb);
        configureServices?.Invoke(builder.Services);
        builder.Services.AddOutboxLanes<TestDbContext>(lanes => lanes.PollInterval = PollInterval);
        builder.Services.AddOutboxProcessing<TestDbContext>(
            new SystemTextJsonOutboxSerializer(),
            outbox =>
            {
                outbox.ProcessIntervalInSeconds = (int)PollInterval.TotalSeconds;
                outbox.DelayInSecondsBeforeStart = (int)PollInterval.TotalSeconds;
            });
        builder.Services.AddQuartzHostedService();

        var host = builder.Build();
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();

        return new TestHost(host);
    }

    public async ValueTask DisposeAsync()
    {
        await Host.StopAsync();
        await using (var scope = Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureDeletedAsync();
        Host.Dispose();
    }

    /// <summary>Saves a new shard through the unit of work; <paramref name="change"/> raises its events.</summary>
    public async Task<Guid> SaveShardAsync(Action<Shard> change)
    {
        var shard = new Shard(Guid.NewGuid(), $"shard-{Guid.NewGuid():N}");
        change(shard);
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestDbContext>().Shards.Add(shard);
        await scope.ServiceProvider
            .GetRequiredService<Resrcify.SharedKernel.Abstractions.UnitOfWork.IUnitOfWork>()
            .CompleteAsync();
        return shard.Id;
    }

    /// <summary>Starts the host and waits until the outbox listener listens.</summary>
    public async Task StartAndListenAsync()
    {
        await Host.StartAsync();
        await WaitForListenerAsync();
    }

    /// <summary>Waits until a connection named as the outbox listener's is open on the database.</summary>
    public async Task WaitForListenerAsync()
    {
        var deadline = TimeProvider.System.GetUtcNow() + TimeSpan.FromSeconds(15);
        while (await CountListenersAsync() == 0)
        {
            if (TimeProvider.System.GetUtcNow() > deadline)
                throw new TimeoutException("The outbox listener didn't connect.");
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    /// <summary>Ends the outbox listener's connection from the server side, as a restart or a failover would.</summary>
    public async Task KillListenerAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDbContext>().Database;
        await database.OpenConnectionAsync();
        await using var command = database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE application_name = 'TestDbContext outbox listener' AND datname = current_database()";
        await command.ExecuteScalarAsync();
    }

    private async Task<long> CountListenersAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDbContext>().Database;
        await database.OpenConnectionAsync();
        await using var command = database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM pg_stat_activity WHERE application_name = 'TestDbContext outbox listener' AND datname = current_database() AND state = 'idle'";
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }
}
