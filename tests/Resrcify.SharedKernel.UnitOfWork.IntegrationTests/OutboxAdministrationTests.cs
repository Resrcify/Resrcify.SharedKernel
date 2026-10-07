using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests;

/// <summary>The outbox administration's queries and updates, as PostgreSQL runs them.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class OutboxAdministrationTests(PostgresFixture pg)
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private ServiceProvider _services = default!;

    public async Task InitializeAsync()
    {
        var connectionString = pg.CreateIsolatedConnectionString();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(Now)));
        services.AddDbContext<TestDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<OutboxAdministration<TestDbContext>>();
        _services = services.BuildServiceProvider();

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
    public async Task GetSummaryAsync_ShouldCountWaitingRetryingAndGivenUpMessages_PerEventType()
    {
        await SeedAsync(
            Message("Shards.Created", minutesAgo: 30),
            Message("Shards.Created", minutesAgo: 10, retryCount: 2),
            Message("Shards.Created", minutesAgo: 5, givenUp: true),
            Message("Payouts.Rotated", minutesAgo: 60, givenUp: true),
            Message("Payouts.Rotated", minutesAgo: 1, processed: true));

        var summary = await Administration(outbox => outbox.GetSummaryAsync());

        summary.Waiting.ShouldBe(2);
        summary.Retrying.ShouldBe(1);
        summary.GivenUp.ShouldBe(2);
        summary.OldestWaitingOccurredOnUtc.ShouldBe(Now.AddMinutes(-30));
        summary.EventTypes.ShouldBe(
        [
            new OutboxEventTypeSummary("Payouts.Rotated", null, Waiting: 0, Retrying: 0, GivenUp: 1),
            new OutboxEventTypeSummary("Shards.Created", null, Waiting: 2, Retrying: 1, GivenUp: 1),
        ]);
    }

    [Fact]
    public async Task ListGivenUpAsync_ShouldListTheMessagesThatGaveUp_MostRecentFirst()
    {
        var older = Message("Shards.Created", minutesAgo: 60, givenUp: true);
        var newer = Message("Payouts.Rotated", minutesAgo: 5, givenUp: true);
        await SeedAsync(older, newer, Message("Shards.Created", minutesAgo: 1));

        var all = await Administration(outbox => outbox.ListGivenUpAsync());
        var ofOneType = await Administration(outbox => outbox.ListGivenUpAsync(type: "Shards.Created"));

        all.Select(message => message.Id).ShouldBe([newer.Id, older.Id]);
        ofOneType.ShouldHaveSingleItem().Error.ShouldBe("Gave up at 2026-10-07 after 3 tries.");
    }

    [Fact]
    public async Task RetryAsync_ShouldMakeAMessageThatGaveUpDueAgain()
    {
        var givenUp = Message("Shards.Created", minutesAgo: 5, givenUp: true);
        givenUp.NextAttemptOnUtc = Now.AddMinutes(4);
        var waiting = Message("Shards.Created", minutesAgo: 1);
        await SeedAsync(givenUp, waiting);

        (await Administration(outbox => outbox.RetryAsync(givenUp.Id))).ShouldBeTrue();
        (await Administration(outbox => outbox.RetryAsync(waiting.Id))).ShouldBeFalse();   // it never gave up

        var retried = await Administration(outbox => outbox.FindAsync(givenUp.Id));
        retried.ShouldNotBeNull();
        retried.ProcessedOnUtc.ShouldBeNull();
        retried.GaveUp.ShouldBeFalse();
        retried.RetryCount.ShouldBe(0);
        retried.NextAttemptOnUtc.ShouldBe(Now);
        retried.Error.ShouldBe("Gave up at 2026-10-07 after 3 tries.");
    }

    [Fact]
    public async Task RetryAllGivenUpAsync_ShouldRetryEveryMessageThatGaveUp_OfTheTypeWhenGiven()
    {
        await SeedAsync(
            Message("Shards.Created", minutesAgo: 5, givenUp: true),
            Message("Shards.Created", minutesAgo: 4, givenUp: true),
            Message("Payouts.Rotated", minutesAgo: 3, givenUp: true));

        (await Administration(outbox => outbox.RetryAllGivenUpAsync("Shards.Created"))).ShouldBe(2);
        (await Administration(outbox => outbox.RetryAllGivenUpAsync())).ShouldBe(1);
        (await Administration(outbox => outbox.GetSummaryAsync())).GivenUp.ShouldBe(0);
    }

    [Fact]
    public async Task FindAsync_ShouldBeNull_ForAnUnknownMessage()
        => (await Administration(outbox => outbox.FindAsync(Guid.NewGuid()))).ShouldBeNull();

    private async Task<T> Administration<T>(Func<OutboxAdministration<TestDbContext>, Task<T>> use)
    {
        await using var scope = _services.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<OutboxAdministration<TestDbContext>>());
    }

    private async Task SeedAsync(params OutboxMessage[] messages)
    {
        await using var scope = _services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        context.OutboxMessages.AddRange(messages);
        await context.SaveChangesAsync();
    }

    private static OutboxMessage Message(
        string type,
        int minutesAgo,
        int retryCount = 0,
        bool givenUp = false,
        bool processed = false)
    {
        DateTime? processedOnUtc = processed ? Now : null;
        return new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            Content = "{}",
            OccurredOnUtc = Now.AddMinutes(-minutesAgo),
            RetryCount = givenUp ? 3 : retryCount,
            ProcessedOnUtc = givenUp ? OutboxMessage.GivenUpProcessedOnUtc : processedOnUtc,
            Error = givenUp ? "Gave up at 2026-10-07 after 3 tries." : null,
        };
    }
}
