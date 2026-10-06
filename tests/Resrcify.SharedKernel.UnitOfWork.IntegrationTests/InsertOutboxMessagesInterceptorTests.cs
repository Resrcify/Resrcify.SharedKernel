using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    private async Task<ServiceProvider> StartAsync(IOutboxInsertStrategy? insertStrategy, bool retryOnFailure)
    {
        var connectionString = pg.CreateIsolatedConnectionString();
        var services = new ServiceCollection()
            .AddDbContext<TestDbContext>(options => options
                .UseNpgsql(connectionString, npgsql =>
                {
                    if (retryOnFailure)
                        npgsql.EnableRetryOnFailure();
                })
                .AddInterceptors(new InsertOutboxMessagesInterceptor(Serializer, insertStrategy)))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        return services;
    }
}
