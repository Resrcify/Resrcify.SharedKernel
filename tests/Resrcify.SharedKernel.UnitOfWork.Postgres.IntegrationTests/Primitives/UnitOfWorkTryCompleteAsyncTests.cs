using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;
using Resrcify.SharedKernel.UnitOfWork.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Primitives;

/// <summary><c>IUnitOfWork.TryCompleteAsync</c> against the failures PostgreSQL really raises.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class UnitOfWorkTryCompleteAsyncTests(PostgresFixture postgres)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task TryCompleteAsync_ShouldReturnAConcurrencyConflict_WhenTheRowChangedSinceItWasRead()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => { });
        var shardId = await host.SaveShardAsync(_ => { });

        // Two requests read the same shard; the first saves its change, the second then saves a stale one.
        await using var first = host.Services.CreateAsyncScope();
        await using var second = host.Services.CreateAsyncScope();
        var firstShard = await Shards(first).SingleAsync(shard => shard.Id == shardId);
        var secondShard = await Shards(second).SingleAsync(shard => shard.Id == shardId);
        firstShard.Rename("first");
        (await UnitOfWork(first).TryCompleteAsync()).IsSuccess.ShouldBeTrue();
        secondShard.Rename("second");

        var result = await UnitOfWork(second).TryCompleteAsync();

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem().ShouldBe(PersistenceErrors.Concurrency);
        result.Errors[0].Type.ShouldBe(ErrorType.Conflict);
        await using var check = host.Services.CreateAsyncScope();
        (await Shards(check).SingleAsync(shard => shard.Id == shardId)).Name.ShouldBe("first");
    }

    [Fact]
    public async Task TryCompleteAsync_ShouldReturnAUniqueViolation_WhenTheSaveDuplicatesAUniqueValue()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => { });
        await using (var seed = host.Services.CreateAsyncScope())
        {
            Shards(seed).Add(new Shard(Guid.NewGuid(), "taken"));
            await UnitOfWork(seed).CompleteAsync();
        }

        await using var scope = host.Services.CreateAsyncScope();
        Shards(scope).Add(new Shard(Guid.NewGuid(), "taken"));
        var result = await UnitOfWork(scope).TryCompleteAsync();

        result.Errors.ShouldHaveSingleItem().ShouldBe(PersistenceErrors.UniqueViolation);
    }

    [Fact]
    public async Task TryCompleteAsync_ShouldReturnTheConflict_InsideATransaction_WhichTheOwnerRollsBack()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => { });
        await using (var seed = host.Services.CreateAsyncScope())
        {
            Shards(seed).Add(new Shard(Guid.NewGuid(), "taken"));
            await UnitOfWork(seed).CompleteAsync();
        }

        await using var scope = host.Services.CreateAsyncScope();
        var unitOfWork = UnitOfWork(scope);
        var result = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                Shards(scope).Add(new Shard(Guid.NewGuid(), "fresh"));
                Shards(scope).Add(new Shard(Guid.NewGuid(), "taken"));
                return await unitOfWork.TryCompleteAsync(token);
            });

        result.Errors.ShouldHaveSingleItem().ShouldBe(PersistenceErrors.UniqueViolation);
        await using var check = host.Services.CreateAsyncScope();
        (await Shards(check).AnyAsync(shard => shard.Name == "fresh")).ShouldBeFalse();
    }

    [Fact]
    public async Task TryCompleteAsync_ShouldSave_WhenNothingConflicts()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => { });
        await using var scope = host.Services.CreateAsyncScope();
        var shardId = Guid.NewGuid();
        Shards(scope).Add(new Shard(shardId, "free"));

        var result = await UnitOfWork(scope).TryCompleteAsync();

        result.IsSuccess.ShouldBeTrue();
        await using var check = host.Services.CreateAsyncScope();
        (await Shards(check).AnyAsync(shard => shard.Id == shardId)).ShouldBeTrue();
    }

    private static DbSet<Shard> Shards(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<TestDbContext>().Shards;

    private static IUnitOfWork UnitOfWork(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
}
