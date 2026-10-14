using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Extensions;

/// <summary><c>AddPostgresDbContext</c> against a real PostgreSQL.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class PostgresServiceCollectionExtensionsTests(PostgresFixture postgres)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AddPostgresDbContext_ShouldStampAuditTimes_FromTheContainersTimeProvider()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        await using var host = await TestHost.CreateAsync(
            postgres,
            db => { },
            services => services.AddSingleton<TimeProvider>(clock));

        var shardId = await host.SaveShardAsync(_ => { });

        await using var scope = host.Services.CreateAsyncScope();
        var shard = await scope.ServiceProvider.GetRequiredService<TestDbContext>().Shards.SingleAsync(x => x.Id == shardId);
        shard.CreatedOnUtc.ShouldBe(clock.GetUtcNow().UtcDateTime);
    }

    /// <summary>
    /// The services insert outbox messages with ON CONFLICT DO NOTHING, which opens a transaction of its own; under a
    /// retrying execution strategy EF Core only allows that inside the strategy, which <c>CompleteAsync</c> provides.
    /// </summary>
    [Fact]
    public async Task RetryOnFailure_ShouldSaveAggregateAndOutboxMessage_ThroughTheUnitOfWork_WithTheOnConflictInsert()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => db
            .WithOutbox(outbox => outbox.OnConflictDoNothing = true)
            .RetryOnFailure());

        var shardId = await host.SaveShardAsync(shard => shard.Rename("retried"));

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        (await context.Shards.AnyAsync(shard => shard.Id == shardId)).ShouldBeTrue();
        (await context.OutboxMessages.CountAsync()).ShouldBe(1);
    }

    /// <summary>
    /// What the README warns about: saving with the context itself bypasses the unit of work's strategy. Sentinel hit it
    /// in a test, with EF Core's "does not support user-initiated transactions", which names neither cause nor fix.
    /// </summary>
    [Fact]
    public async Task RetryOnFailure_ShouldRefuseADirectSaveOfEvents_WithTheOnConflictInsert_SayingWhatToDo()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => db
            .WithOutbox(outbox => outbox.OnConflictDoNothing = true)
            .RetryOnFailure());
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var shard = new Shard(Guid.NewGuid(), "direct");
        shard.Rename("direct save");
        context.Shards.Add(shard);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        thrown.Message.ShouldContain("Save through IUnitOfWork.CompleteAsync");
        await using var check = host.Services.CreateAsyncScope();
        var database = check.ServiceProvider.GetRequiredService<TestDbContext>();
        (await database.Shards.AnyAsync(saved => saved.Id == shard.Id)).ShouldBeFalse();
        (await database.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    /// <summary>The way out the error names, besides the unit of work: the save run under the execution strategy.</summary>
    [Fact]
    public async Task RetryOnFailure_ShouldSaveEventsDirectly_UnderTheExecutionStrategy_WithTheOnConflictInsert()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => db
            .WithOutbox(outbox => outbox.OnConflictDoNothing = true)
            .RetryOnFailure());
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var shard = new Shard(Guid.NewGuid(), "direct");
        shard.Rename("direct save");
        context.Shards.Add(shard);

        await context.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(token => context.SaveChangesAsync(token), CancellationToken.None);

        (await context.OutboxMessages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task HasPostgresRowVersion_ShouldNeedNoColumn_AndReadXmin()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => { });
        var shardId = await host.SaveShardAsync(_ => { });

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var shard = await context.Shards.SingleAsync(x => x.Id == shardId);

        context.Entry(shard).Property<uint>("xmin").CurrentValue.ShouldBeGreaterThan(0u);
        var columns = await context.Database
            .SqlQueryRaw<string>("SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'Shards'")
            .ToListAsync();
        columns.ShouldNotContain("xmin");
    }
}
