using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class CleanupOutboxMessagesJobTests(PostgresFixture pg)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private ServiceProvider _services = default!;

    public async Task InitializeAsync()
    {
        var connectionString = pg.CreateIsolatedConnectionString();
        _services = new ServiceCollection()
            .AddDbContext<TestDbContext>(options => options.UseNpgsql(connectionString))
            .BuildServiceProvider();
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var scope = _services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureDeletedAsync();
        await _services.DisposeAsync();
    }

    [Fact]
    public async Task Execute_ShouldDeleteOnlyProcessedMessages_WhenTheyAreOlderThanTheRetention()
    {
        var now = DateTime.UtcNow;
        var processedLongAgo = Message(occurred: now.AddDays(-9), processed: now.AddDays(-8));
        var processedRecently = Message(occurred: now.AddDays(-2), processed: now.AddDays(-1));
        var waitingLongAgo = Message(occurred: now.AddDays(-30), processed: null);
        var poison = Message(occurred: now.AddDays(-30), processed: null, retryCount: 3);
        await SaveAsync(processedLongAgo, processedRecently, waitingLongAgo, poison);

        await RunJobAsync(retentionInDays: 7);

        (await RemainingIdsAsync()).ShouldBe([processedRecently.Id, waitingLongAgo.Id, poison.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Execute_ShouldDeleteEverythingOld_WhenItTakesMoreThanOneBatch()
    {
        var now = DateTime.UtcNow;
        var old = Enumerable.Range(0, CleanupOutboxMessagesJob<TestDbContext>.DeleteBatchSize + 10)
            .Select(_ => Message(occurred: now.AddDays(-10), processed: now.AddDays(-9)))
            .ToArray();
        await SaveAsync(old);

        await RunJobAsync(retentionInDays: 7);

        (await RemainingIdsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Execute_ShouldKeepAProcessedMessage_UntilTheClockPassesTheRetention()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        var processed = Message(occurred: clock.GetUtcNow().UtcDateTime, processed: clock.GetUtcNow().UtcDateTime);
        await SaveAsync(processed);

        clock.Advance(TimeSpan.FromDays(6));
        await RunJobAsync(retentionInDays: 7, clock);
        (await RemainingIdsAsync()).ShouldBe([processed.Id]);

        clock.Advance(TimeSpan.FromDays(2));
        await RunJobAsync(retentionInDays: 7, clock);
        (await RemainingIdsAsync()).ShouldBeEmpty();
    }

    private async Task RunJobAsync(int retentionInDays, TimeProvider? clock = null)
    {
        var job = new CleanupOutboxMessagesJob<TestDbContext>(
            _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CleanupOutboxMessagesJob<TestDbContext>>.Instance,
            clock);
        var context = Substitute.For<IJobExecutionContext>();
        context.MergedJobDataMap.Returns(new JobDataMap { ["ProcessedRetentionInDays"] = retentionInDays });
        await job.Execute(context);
    }

    private async Task SaveAsync(params OutboxMessage[] messages)
    {
        await using var scope = _services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        context.Set<OutboxMessage>().AddRange(messages);
        await context.SaveChangesAsync();
    }

    private async Task<Guid[]> RemainingIdsAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>()
            .Set<OutboxMessage>()
            .Select(message => message.Id)
            .ToArrayAsync();
    }

    private static OutboxMessage Message(DateTime occurred, DateTime? processed, int retryCount = 0)
        => new()
        {
            Id = Guid.NewGuid(),
            Type = "Test",
            Content = "{}",
            OccurredOnUtc = occurred,
            ProcessedOnUtc = processed,
            RetryCount = retryCount,
        };
}
