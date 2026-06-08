using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
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

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class UnitOfWorkTransactionAtomicityTests
    : IClassFixture<PostgresFixture>
{
    private const string DefaultStrategyKey = "default";
    private const string PgOnConflictStrategyKey = "pg-on-conflict";

    private static readonly SystemTextJsonOutboxSerializer _serializer = new();

    private readonly PostgresFixture _pg;

    public UnitOfWorkTransactionAtomicityTests(PostgresFixture pg) => _pg = pg;

    [Theory]
    [InlineData(DefaultStrategyKey)]
    [InlineData(PgOnConflictStrategyKey)]
    public async Task ExecuteInTransactionAsync_OnSuccessResult_CommitsBothAggregateAndOutboxRow(
        string strategyKey)
    {
        // Arrange
        await using var services = await BuildServicesAsync(strategyKey);
        var aggregateId = Guid.NewGuid();

        // Act — pipeline-managed transaction, handler returns success
        await using (var scope = services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var result = await uow.ExecuteInTransactionAsync<Result>(
                async _ =>
                {
                    var aggregate = new TestAggregate(aggregateId, "success-path");
                    aggregate.ChangeName("renamed");
                    ctx.Aggregates.Add(aggregate);
                    return Result.Success();
                });

            result.IsSuccess.ShouldBeTrue();
        }

        // Assert — both aggregate and outbox row committed
        await using (var scope = services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            (await ctx.Aggregates.AsNoTracking().CountAsync()).ShouldBe(1);
            (await ctx.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(1);
        }
    }

    [Theory]
    [InlineData(DefaultStrategyKey)]
    [InlineData(PgOnConflictStrategyKey)]
    public async Task ExecuteInTransactionAsync_OnFailureResult_RollsBackEverything(
        string strategyKey)
    {
        // Arrange
        await using var services = await BuildServicesAsync(strategyKey);
        var aggregateId = Guid.NewGuid();

        // Act — handler returns failure; pipeline rolls back without invoking SaveChanges
        await using (var scope = services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var result = await uow.ExecuteInTransactionAsync<Result>(
                async _ =>
                {
                    var aggregate = new TestAggregate(aggregateId, "failure-path");
                    aggregate.ChangeName("renamed");
                    ctx.Aggregates.Add(aggregate);
                    return Result.Failure(Error.Failure("test", "intentional failure"));
                });

            result.IsFailure.ShouldBeTrue();
        }

        // Assert — neither row exists
        await using (var scope = services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            (await ctx.Aggregates.AsNoTracking().CountAsync()).ShouldBe(0);
            (await ctx.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(0);
        }
    }

    [Theory]
    [InlineData(DefaultStrategyKey)]
    [InlineData(PgOnConflictStrategyKey)]
    public async Task ExecuteInTransactionAsync_WhenHandlerThrows_RollsBackAndRethrows(
        string strategyKey)
    {
        // Arrange
        await using var services = await BuildServicesAsync(strategyKey);
        var aggregateId = Guid.NewGuid();
        var sentinel = new InvalidOperationException("boom");

        // Act — handler calls SaveChanges then throws
        await using (var scope = services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var act = async () => await uow.ExecuteInTransactionAsync<Result>(
                async ct =>
                {
                    var aggregate = new TestAggregate(aggregateId, "throw-path");
                    aggregate.ChangeName("renamed");
                    ctx.Aggregates.Add(aggregate);
                    await ctx.SaveChangesAsync(ct);
                    throw sentinel;
                });

            var thrown = await act.ShouldThrowAsync<InvalidOperationException>();
            thrown.ShouldBeSameAs(sentinel);
        }

        // Assert — rollback wiped both rows; original exception preserved
        await using (var scope = services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            (await ctx.Aggregates.AsNoTracking().CountAsync()).ShouldBe(0);
            (await ctx.OutboxMessages.AsNoTracking().CountAsync()).ShouldBe(0);
        }
    }

    [Theory]
    [InlineData(DefaultStrategyKey)]
    [InlineData(PgOnConflictStrategyKey)]
    public async Task ExecuteInTransactionAsync_InterceptorDefers_LeavingPipelineTransactionIntact(
        string strategyKey)
    {
        // Arrange
        await using var services = await BuildServicesAsync(strategyKey);

        await using var scope = services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Act + Assert — CurrentTransaction must be the same reference before and after SaveChanges
        await uow.ExecuteInTransactionAsync<Result>(
            async ct =>
            {
                var txBeforeSave = ctx.Database.CurrentTransaction;
                txBeforeSave.ShouldNotBeNull();

                var aggregate = new TestAggregate(Guid.NewGuid(), "defer-check");
                aggregate.ChangeName("renamed");
                ctx.Aggregates.Add(aggregate);

                await ctx.SaveChangesAsync(ct);

                var txAfterSave = ctx.Database.CurrentTransaction;
                txAfterSave.ShouldBeSameAs(txBeforeSave);

                return Result.Success();
            });
    }

    private async Task<ServiceProvider> BuildServicesAsync(string strategyKey)
    {
        IOutboxInsertStrategy? strategy = strategyKey switch
        {
            DefaultStrategyKey => null,
            PgOnConflictStrategyKey => new PostgresOnConflictOutboxInsertStrategy(),
            _ => throw new ArgumentOutOfRangeException(nameof(strategyKey), strategyKey, null)
        };

        var collection = new ServiceCollection();

        var connectionString = _pg.CreateIsolatedConnectionString();
        collection.AddDbContext<TestDbContext>(options =>
            options
                .UseNpgsql(connectionString)
                .AddInterceptors(new InsertOutboxMessagesInterceptor(_serializer, strategy)));

        collection.AddScoped<IUnitOfWork, UnitOfWork<TestDbContext>>();

        var services = collection.BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        return services;
    }
}
