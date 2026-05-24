using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests;

/// <summary>
/// Race-proof variant of the outbox dedup contract — backed by a real partial
/// unique index in Postgres and the <see cref="PostgresOnConflictOutboxInsertStrategy"/>
/// so the SaveChanges call survives concurrent duplicate inserts without a
/// <see cref="DbUpdateException"/> tearing down the user transaction.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class OutboxDedupTests
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly SystemTextJsonOutboxSerializer _serializer = new();

    private readonly PostgresFixture _pg;
    private ServiceProvider _services = default!;

    public OutboxDedupTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        var collection = new ServiceCollection();

        var connectionString = _pg.CreateIsolatedConnectionString();
        collection.AddDbContext<TestDbContext>(options =>
            options
                .UseNpgsql(connectionString)
                .AddInterceptors(new InsertOutboxMessagesInterceptor(
                    _serializer,
                    new PostgresOnConflictOutboxInsertStrategy())));

        _services = collection.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await ctx.Database.EnsureDeletedAsync();
        }

        await _services.DisposeAsync();
    }

    [Fact]
    public async Task PartialUniqueIndex_OnDedupKey_IsCreatedByConfiguration()
    {
        // Proves the test fixture actually has the strict constraint that
        // ON CONFLICT relies on — otherwise the rest of these tests would
        // succeed for the wrong reason.
        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var indexDef = await ctx.Database
            .SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes "
                + "WHERE schemaname = {0} AND indexname = {1}",
                TestDbContext.OutboxSchema,
                "IX_OutboxMessages_Dedup")
            .SingleOrDefaultAsync();

        indexDef.ShouldNotBeNull();
        indexDef.ShouldContain("UNIQUE", Case.Insensitive);
        indexDef.ShouldContain("DedupKey");
        indexDef.ShouldContain("WHERE", Case.Insensitive);
    }

    [Fact]
    public async Task PostgresStrategy_PersistsFirstDedupableEvent_WithComposedKey()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid(), "init");
        aggregate.RaiseDedupableEvent("shard-1");

        await using var scope = _services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        ctx.Aggregates.Add(aggregate);

        // Act
        await ctx.SaveChangesAsync();

        // Assert
        var rows = await ctx.OutboxMessages.AsNoTracking().ToListAsync();
        rows.ShouldHaveSingleItem();
        rows[0].DedupKey.ShouldBe($"{typeof(TestDedupableEvent).FullName}:shard-1");
    }

    [Fact]
    public async Task PostgresStrategy_SilentlySwallowsRaceDuplicate_WithoutThrowing()
    {
        // Arrange — first transaction commits a dedupable row
        var first = new TestAggregate(Guid.NewGuid(), "first");
        first.RaiseDedupableEvent("shard-1");

        await using (var firstScope = _services.CreateAsyncScope())
        {
            var ctx = firstScope.ServiceProvider.GetRequiredService<TestDbContext>();
            ctx.Aggregates.Add(first);
            await ctx.SaveChangesAsync();
        }

        // Act — second transaction tries the same dedup key from a fresh DbContext.
        // The interceptor's pre-check sees the first row and skips. But to actually
        // prove ON CONFLICT works, we bypass the pre-check by inserting an outbox
        // row directly via the strategy — simulating the race where two transactions
        // both passed the pre-check at the same instant.
        await using (var secondScope = _services.CreateAsyncScope())
        {
            var ctx = secondScope.ServiceProvider.GetRequiredService<TestDbContext>();
            var strategy = new PostgresOnConflictOutboxInsertStrategy();
            var dupe = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = typeof(TestDedupableEvent).FullName!,
                Content = "{}",
                OccurredOnUtc = DateTime.UtcNow,
                DedupKey = $"{typeof(TestDedupableEvent).FullName}:shard-1",
            };

            // Should NOT throw despite the unique-index conflict.
            var act = async () =>
                await strategy.InsertAsync(ctx, [dupe], default);

            await act.ShouldNotThrowAsync();
        }

        // Assert — only the first row landed; the race-duplicate was swallowed by ON CONFLICT.
        await using (var verifyScope = _services.CreateAsyncScope())
        {
            var ctx = verifyScope.ServiceProvider.GetRequiredService<TestDbContext>();
            var rows = await ctx.OutboxMessages.AsNoTracking().ToListAsync();
            rows.ShouldHaveSingleItem();
        }
    }

    [Fact]
    public async Task PostgresStrategy_AllowsInsert_AfterOriginalRowIsMarkedProcessed()
    {
        // Arrange — first dedupable row, then mark processed
        var aggregate = new TestAggregate(Guid.NewGuid(), "first");
        aggregate.RaiseDedupableEvent("shard-1");

        await using (var scope = _services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            ctx.Aggregates.Add(aggregate);
            await ctx.SaveChangesAsync();

            var row = await ctx.OutboxMessages.SingleAsync();
            row.ProcessedOnUtc = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
        }

        // Act — fresh aggregate, same dedup key. Partial unique index filters on
        // ProcessedOnUtc IS NULL, so the previous row no longer collides.
        var second = new TestAggregate(Guid.NewGuid(), "second");
        second.RaiseDedupableEvent("shard-1");

        await using (var scope = _services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            ctx.Aggregates.Add(second);
            await ctx.SaveChangesAsync();
        }

        // Assert — two rows now: first (processed), second (unprocessed)
        await using (var scope = _services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var rows = await ctx.OutboxMessages.AsNoTracking()
                .OrderBy(m => m.OccurredOnUtc)
                .ToListAsync();
            rows.Count.ShouldBe(2);
            rows[0].ProcessedOnUtc.ShouldNotBeNull();
            rows[1].ProcessedOnUtc.ShouldBeNull();
        }
    }

}
