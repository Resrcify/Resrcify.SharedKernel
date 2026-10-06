using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Mediator;
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
        IPublisher publisher,
        QueryCounter queries,
        TimeProvider? clock)
    {
        _connection = connection;
        _provider = provider;
        Publisher = publisher;
        Queries = queries;
        Job = new ProcessOutboxMessagesJob<TestDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            provider.GetService<OutboxWakeUp<TestDbContext>>());
    }

    internal IPublisher Publisher { get; }

    internal ProcessOutboxMessagesJob<TestDbContext> Job { get; }

    internal IServiceProvider Services => _provider;

    /// <summary>The SELECTs the job's contexts ran.</summary>
    internal QueryCounter Queries { get; }

    /// <param name="clock">The clock the job measures its drain budget on.</param>
    /// <param name="configure">More services (an outbox wake-up, a scheduler).</param>
    internal static async Task<OutboxJobTestHarness> CreateAsync(
        TimeProvider? clock = null,
        Action<IServiceCollection>? configure = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var publisher = Substitute.For<IPublisher>();
        var queries = new QueryCounter();

        var services = new ServiceCollection();
        services.AddScoped(_ => new TestDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(queries)
                .Options));
        services.AddScoped<IUnitOfWork, UnitOfWork<TestDbContext>>();
        services.AddSingleton(publisher);
        services.AddSingleton<IOutboxSerializer>(new SystemTextJsonOutboxSerializer());
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider
                .GetRequiredService<TestDbContext>()
                .Database
                .EnsureCreatedAsync();

        return new OutboxJobTestHarness(connection, provider, publisher, queries, clock);
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
        int maxRetryCount = 3,
        int? intervalInSeconds = null)
    {
        var data = new JobDataMap
        {
            { "ProcessBatchSize", batchSize },
            { "ProcessMaxRetryCount", maxRetryCount },
        };
        if (intervalInSeconds is { } interval)
            data.Add("ProcessIntervalInSeconds", interval);
        var jobContext = Substitute.For<IJobExecutionContext>();
        jobContext.MergedJobDataMap.Returns(data);
        return jobContext;
    }

    /// <summary>
    /// A context whose job data is exactly <paramref name="data"/>, for exercising how the job reads values
    /// that were not stored as ints (or not stored at all).
    /// </summary>
    internal static IJobExecutionContext JobContext(JobDataMap data)
    {
        var jobContext = Substitute.For<IJobExecutionContext>();
        jobContext.MergedJobDataMap.Returns(data);
        return jobContext;
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>Counts the SELECT commands run by the contexts it is added to.</summary>
internal sealed class QueryCounter : DbCommandInterceptor
{
    private int _selects;
    private int _readsDone;

    /// <summary>How many SELECTs have run.</summary>
    public int Selects => Volatile.Read(ref _selects);

    /// <summary>How many result sets have been read to the end (their readers closed).</summary>
    public int ReadsDone => Volatile.Read(ref _readsDone);

    public override ValueTask<InterceptionResult> DataReaderClosingAsync(
        DbCommand command,
        DataReaderClosingEventData eventData,
        InterceptionResult result)
    {
        Interlocked.Increment(ref _readsDone);
        return base.DataReaderClosingAsync(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            Interlocked.Increment(ref _selects);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
