using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

/// <summary>
/// An outbox on in-memory SQLite with a backlog monitor on a fake clock, so its measurements (and the health check
/// reading them) can be tested at exact times.
/// </summary>
internal sealed class OutboxBacklogTestHost : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    private OutboxBacklogTestHost(SqliteConnection connection, ServiceProvider provider, FakeTimeProvider clock)
    {
        _connection = connection;
        _provider = provider;
        Clock = clock;
        Monitor = new OutboxBacklogMonitor<TestDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new OutboxBacklogSettings<TestDbContext>(MaxRetryCount: 3, Interval: TimeSpan.FromSeconds(30)),
            NullLogger<OutboxBacklogMonitor<TestDbContext>>.Instance,
            clock);
    }

    public FakeTimeProvider Clock { get; }

    public OutboxBacklogMonitor<TestDbContext> Monitor { get; }

    /// <param name="laneEvent">An event type in a lane whose messages get <paramref name="laneMaxRetryCount"/> tries.</param>
    public static async Task<OutboxBacklogTestHost> CreateAsync(Type? laneEvent = null, int laneMaxRetryCount = 3)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options));
        if (laneEvent is not null)
        {
            services.AddSingleton<IOutboxLaneEvent>(new OutboxLaneEvent("slow", laneEvent));
            services.AddSingleton<OutboxLaneRegistry>();
            services.AddSingleton(new OutboxLaneSettings<TestDbContext>(new OutboxLaneOptions { MaxRetryCount = laneMaxRetryCount }));
        }
        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        return new OutboxBacklogTestHost(connection, provider, new FakeTimeProvider(Start));
    }

    public static OutboxMessage Message(
        TimeSpan occurredAgo,
        int retryCount = 0,
        bool processed = false,
        string type = "Shard.Events.RankChanged")
        => new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            Content = "{}",
            OccurredOnUtc = (Start - occurredAgo).UtcDateTime,
            ProcessedOnUtc = processed ? Start.UtcDateTime : null,
            RetryCount = retryCount,
        };

    public async Task SeedAsync(params OutboxMessage[] messages)
    {
        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        context.OutboxMessages.AddRange(messages);
        await context.SaveChangesAsync();
    }

    /// <summary>Drops the outbox table, as a database without the migrations would be: every measurement fails.</summary>
    public async Task DropOutboxTableAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE \"OutboxMessages\"");
    }

    public async ValueTask DisposeAsync()
    {
        Monitor.Dispose();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
