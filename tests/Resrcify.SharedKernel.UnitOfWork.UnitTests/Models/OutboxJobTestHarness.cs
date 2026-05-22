using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

/// <summary>
/// Spins up a real DI container backed by a shared in-memory SQLite database so the
/// outbox job can be exercised exactly as it runs in production: resolving a fresh
/// scope (and DbContext) per message.
/// </summary>
internal sealed class OutboxJobTestHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    private OutboxJobTestHarness(
        SqliteConnection connection,
        ServiceProvider provider,
        IPublisher publisher)
    {
        _connection = connection;
        _provider = provider;
        Publisher = publisher;
        Job = new ProcessOutboxMessagesJob<TestDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>());
    }

    internal IPublisher Publisher { get; }

    internal ProcessOutboxMessagesJob<TestDbContext> Job { get; }

    internal static async Task<OutboxJobTestHarness> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var publisher = Substitute.For<IPublisher>();

        var services = new ServiceCollection();
        services.AddScoped(_ => new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite(connection)
                .Options));
        services.AddScoped<IUnitOfWork, UnitOfWork<TestDbContext>>();
        services.AddSingleton(publisher);
        services.AddSingleton<IOutboxSerializer>(new SystemTextJsonOutboxSerializer());

        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider
                .GetRequiredService<TestDbContext>()
                .Database
                .EnsureCreatedAsync();

        return new OutboxJobTestHarness(connection, provider, publisher);
    }

    internal async Task SeedAsync(params OutboxMessage[] messages)
    {
        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await context.Set<OutboxMessage>().AddRangeAsync(messages);
        await context.SaveChangesAsync();
    }

    internal async Task<List<OutboxMessage>> GetMessagesAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        return await context.Set<OutboxMessage>().AsNoTracking().ToListAsync();
    }

    internal static IJobExecutionContext JobContext(
        int batchSize = 10,
        int maxRetryCount = 3)
    {
        var jobContext = Substitute.For<IJobExecutionContext>();
        jobContext.MergedJobDataMap.Returns(new JobDataMap
        {
            { "ProcessBatchSize", batchSize },
            { "ProcessMaxRetryCount", maxRetryCount },
        });
        return jobContext;
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
