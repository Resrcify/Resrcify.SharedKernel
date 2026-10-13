using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests;

/// <summary>
/// The outbox interceptor under a retrying execution strategy, after a failed save, and on the synchronous save.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class InsertOutboxMessagesInterceptorTests(PostgresFixture pg)
    : IClassFixture<PostgresFixture>
{
    private static readonly SystemTextJsonOutboxSerializer Serializer = new();

    [Fact]
    public async Task SaveChangesAsync_ShouldWriteTheOutboxMessage_WhenTheContextRetriesOnFailure()
    {
        await using var services = await StartAsync(insertStrategy: null, retryOnFailure: true);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var aggregate = new TestAggregate(Guid.NewGuid(), "start");
        aggregate.ChangeName("renamed");
        context.Aggregates.Add(aggregate);
        await context.SaveChangesAsync();

        (await context.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldWork_WhenTheContextRetriesOnFailureAndNothingRaisedEvents()
    {
        await using var services = await StartAsync(new PostgresOnConflictOutboxInsertStrategy(), retryOnFailure: true);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        context.Aggregates.Add(new TestAggregate(Guid.NewGuid(), "no-events"));
        await context.SaveChangesAsync();

        (await context.Aggregates.AsNoTracking().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ShouldCommitTheOutboxMessage_WhenTheContextRetriesOnFailure()
    {
        await using var services = await StartAsync(new PostgresOnConflictOutboxInsertStrategy(), retryOnFailure: true);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        using var unitOfWork = new UnitOfWork<TestDbContext>(context);

        await unitOfWork.ExecuteInTransactionAsync(token =>
        {
            var aggregate = new TestAggregate(Guid.NewGuid(), "start");
            aggregate.ChangeName("renamed");
            context.Aggregates.Add(aggregate);
            return Task.FromResult(Result.Success());
        });

        (await context.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChangesAsync_ShouldKeepTheEventsAndWriteThemOnce_WhenTheFirstSaveFailedAndIsRetried(bool onConflictStrategy)
    {
        await using var services = await StartAsync(
            onConflictStrategy ? new PostgresOnConflictOutboxInsertStrategy() : null,
            retryOnFailure: false);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var aggregate = new TestAggregate(Guid.NewGuid(), new string('x', 100));   // longer than the column: the save fails
        aggregate.ChangeName(new string('y', 100));
        context.Aggregates.Add(aggregate);
        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());

        aggregate.GetDomainEvents().Count.ShouldBe(1);
        aggregate.ChangeName("fits");
        await context.SaveChangesAsync();

        (await context.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(2);   // the two events, each once
        aggregate.GetDomainEvents().ShouldBeEmpty();
        context.Database.CurrentTransaction.ShouldBeNull();
    }

    [Fact]
    public async Task SaveChanges_ShouldThrowRatherThanDropTheEvents_WhenSavedSynchronously()
    {
        await using var services = await StartAsync(insertStrategy: null, retryOnFailure: false);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var aggregate = new TestAggregate(Guid.NewGuid(), "start");
        aggregate.ChangeName("renamed");
        context.Aggregates.Add(aggregate);

        Should.Throw<NotSupportedException>(() => context.SaveChanges());
        aggregate.GetDomainEvents().Count.ShouldBe(1);
    }

    [Fact]
    public async Task CompleteAsync_ShouldWriteEverythingAgain_WhenTheOwnTransactionsCommitFailsAndIsRetried()
    {
        var failCommit = new FailFirstCommit();
        await using var services = await StartAsync(new PostgresOnConflictOutboxInsertStrategy(), retryOnFailure: true, failCommit);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        using var unitOfWork = new UnitOfWork<TestDbContext>(context);
        var aggregate = new TestAggregate(Guid.NewGuid(), "start");
        aggregate.ChangeName("renamed");
        context.Aggregates.Add(aggregate);
        failCommit.Arm();

        await unitOfWork.CompleteAsync();

        // The retry used to save nothing (EF had accepted the changes) and report success with nothing committed.
        failCommit.Failed.ShouldBeTrue();
        await using var check = services.CreateAsyncScope();
        var fresh = check.ServiceProvider.GetRequiredService<TestDbContext>();
        (await fresh.Aggregates.AsNoTracking().SingleAsync()).Name.ShouldBe("renamed");
        (await fresh.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(1);
        aggregate.GetDomainEvents().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChangesAsync_ShouldWriteTheEventOnce_WhenAConcurrencyConflictIsResolvedAndSavedAgain(bool onConflictStrategy)
    {
        await using var services = await StartAsync(
            onConflictStrategy ? new PostgresOnConflictOutboxInsertStrategy() : null,
            retryOnFailure: false);
        var id = Guid.NewGuid();
        await using (var seed = services.CreateAsyncScope())
        {
            var seedContext = seed.ServiceProvider.GetRequiredService<TestDbContext>();
            seedContext.Aggregates.Add(new TestAggregate(id, "start"));
            await seedContext.SaveChangesAsync();
        }
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var aggregate = await context.Aggregates.SingleAsync(a => a.Id == id);
        await ExecuteElsewhereAsync(services, "DELETE FROM \"Aggregates\"");
        aggregate.ChangeName("renamed");

        // The row is gone (0 rows updated): EF doesn't tell interceptors about this failure.
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
        await ExecuteElsewhereAsync(services, $"INSERT INTO \"Aggregates\" (\"Id\", \"Name\") VALUES ('{id}', 'start')");
        await context.SaveChangesAsync();

        context.Database.CurrentTransaction.ShouldBeNull();
        await using var check = services.CreateAsyncScope();
        var fresh = check.ServiceProvider.GetRequiredService<TestDbContext>();
        (await fresh.Aggregates.AsNoTracking().SingleAsync()).Name.ShouldBe("renamed");
        (await fresh.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldTakeBackTheOutboxRows_WhenASaveFailsInsideTheCallersTransaction()
    {
        await using var services = await StartAsync(new PostgresOnConflictOutboxInsertStrategy(), retryOnFailure: false);
        var takenId = Guid.NewGuid();
        await using (var seed = services.CreateAsyncScope())
        {
            var seedContext = seed.ServiceProvider.GetRequiredService<TestDbContext>();
            seedContext.Aggregates.Add(new TestAggregate(takenId, "taken"));
            await seedContext.SaveChangesAsync();
        }
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var duplicate = new TestAggregate(takenId, "duplicate");
        duplicate.ChangeName("duplicate-renamed");
        context.Aggregates.Add(duplicate);

        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());   // 23505 on the id
        context.Entry(duplicate).State = EntityState.Detached;
        var fixedUp = new TestAggregate(Guid.NewGuid(), "fixed");
        fixedUp.ChangeName("fixed-renamed");
        context.Aggregates.Add(fixedUp);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        await using var check = services.CreateAsyncScope();
        var fresh = check.ServiceProvider.GetRequiredService<TestDbContext>();
        var written = await fresh.OutboxMessages.AsNoTracking().Select(message => message.Content).ToListAsync();
        written.ShouldHaveSingleItem().ShouldContain("fixed-renamed");
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldWriteTheEventsOfOneSaveInTheOrderRaised()
    {
        await using var services = await StartAsync(insertStrategy: null, retryOnFailure: false);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var aggregate = new TestAggregate(Guid.NewGuid(), "start");
        var raised = Enumerable.Range(1, 12).Select(rank => $"rank-{rank}").ToList();
        foreach (var name in raised)
            aggregate.ChangeName(name);
        context.Aggregates.Add(aggregate);

        await context.SaveChangesAsync();

        var inOutboxOrder = await context.OutboxMessages
            .AsNoTracking()
            .OrderBy(message => message.OccurredOnUtc)
            .Select(message => message.Content)
            .ToListAsync();
        inOutboxOrder
            .Select(content => ((TestNameChangedEvent)Serializer.Deserialize(content)!).NewName)
            .ShouldBe(raised);
    }

    private static async Task ExecuteElsewhereAsync(ServiceProvider services, string sql)
    {
        await using var scope = services.CreateAsyncScope();
        var other = scope.ServiceProvider.GetRequiredService<TestDbContext>();
#pragma warning disable EF1002 // test SQL built from a Guid this test made
        await other.Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
    }

    private async Task<ServiceProvider> StartAsync(
        IOutboxInsertStrategy? insertStrategy,
        bool retryOnFailure,
        params IInterceptor[] moreInterceptors)
    {
        var connectionString = pg.CreateIsolatedConnectionString();
        var services = new ServiceCollection()
            .AddDbContext<TestDbContext>(options => options
                .UseNpgsql(connectionString, npgsql =>
                {
                    if (retryOnFailure)
                        npgsql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(50), null);
                })
                .AddInterceptors(new InsertOutboxMessagesInterceptor(Serializer, insertStrategy))
                .AddInterceptors(moreInterceptors))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        return services;
    }

    /// <summary>Fails the first commit after <see cref="Arm"/> with a serialization failure (40001), which is transient.</summary>
    private sealed class FailFirstCommit : DbTransactionInterceptor
    {
        private int _armed;

        public bool Failed { get; private set; }

        public void Arm()
            => Interlocked.Exchange(ref _armed, 1);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Failed = true;
                throw new PostgresException("Injected serialization failure at commit.", "ERROR", "ERROR", "40001");
            }

            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }
}
