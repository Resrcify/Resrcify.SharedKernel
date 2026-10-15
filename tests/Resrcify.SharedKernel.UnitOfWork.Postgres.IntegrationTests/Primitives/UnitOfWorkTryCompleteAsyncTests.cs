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

    [Fact]
    public async Task TryCompleteAsync_ShouldReturnANotNullViolation_NamingTheTableAndTheColumn()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => { });
        var shardId = await host.SaveShardAsync(_ => { });

        var error = await RefusedAsync(host, new Member(Guid.NewGuid(), shardId, nickname: null, rank: 1));

        error.Code.ShouldBe(PersistenceErrors.NotNullViolation.Code);
        error.Type.ShouldBe(ErrorType.Validation);
        error.Message.ShouldBe("A value the database requires is missing (table \"Members\", column \"Nickname\").");
    }

    [Fact]
    public async Task TryCompleteAsync_ShouldReturnACheckViolation_NamingTheConstraintAndTheTable()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => { });
        var shardId = await host.SaveShardAsync(_ => { });

        var error = await RefusedAsync(host, new Member(Guid.NewGuid(), shardId, "minus", rank: -7));

        error.Code.ShouldBe(PersistenceErrors.CheckViolation.Code);
        error.Type.ShouldBe(ErrorType.Validation);
        error.Message.ShouldBe(
            $"A value breaks a rule the database enforces (constraint \"{Member.RankConstraint}\", table \"Members\").");
        error.Message.ShouldNotContain("-7");
    }

    [Fact]
    public async Task TryCompleteAsync_ShouldReturnAValueTooLong_WithoutTheValue()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => { });
        var shardId = await host.SaveShardAsync(_ => { });
        var tooLong = new string('x', Member.NicknameMaxLength + 1);

        var error = await RefusedAsync(host, new Member(Guid.NewGuid(), shardId, tooLong, rank: 1));

        error.Code.ShouldBe(PersistenceErrors.ValueTooLong.Code);
        error.Type.ShouldBe(ErrorType.Validation);
        error.Message.ShouldStartWith("A value is longer than the database allows");
        error.Message.ShouldNotContain(tooLong);
    }

    [Fact]
    public async Task TryCompleteAsync_ShouldReturnAForeignKeyViolation_WhenTheSaveRefersToARowThatDoesNotExist()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => { });
        await host.SaveShardAsync(_ => { });
        var missingShardId = Guid.NewGuid();

        var error = await RefusedAsync(host, new Member(Guid.NewGuid(), missingShardId, "orphan", rank: 1));

        error.Code.ShouldBe(PersistenceErrors.ForeignKeyViolation.Code);
        error.Type.ShouldBe(ErrorType.Conflict);
        error.Message.ShouldBe(
            "The data refers to data that doesn't exist, or other data still refers to it "
            + $"(constraint \"{Member.ShardConstraint}\", table \"Members\").");
        error.Message.ShouldNotContain(missingShardId.ToString());
    }

    [Fact]
    public async Task TryCompleteAsync_ShouldReturnAForeignKeyViolation_WhenTheSaveDeletesARowARestrictKeyRefersTo()
    {
        await using var host = await TestHost.CreateAsync(postgres, db => { });
        var shardId = await host.SaveShardAsync(_ => { });
        await using (var seed = host.Services.CreateAsyncScope())
        {
            Context(seed).Members.Add(new Member(Guid.NewGuid(), shardId, "member", rank: 1));
            await UnitOfWork(seed).CompleteAsync();
        }

        await using var scope = host.Services.CreateAsyncScope();
        var shard = await Shards(scope).SingleAsync(x => x.Id == shardId);
        Shards(scope).Remove(shard);
        var result = await UnitOfWork(scope).TryCompleteAsync();

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(PersistenceErrors.ForeignKeyViolation.Code);
        error.Type.ShouldBe(ErrorType.Conflict);
        error.Message.ShouldContain($"constraint \"{Member.ShardConstraint}\"");
        // The refused delete is undone in the change tracker: the shard is tracked unchanged again.
        Context(scope).Entry(shard).State.ShouldBe(EntityState.Unchanged);
        (await UnitOfWork(scope).TryCompleteAsync()).IsSuccess.ShouldBeTrue();
        await using var check = host.Services.CreateAsyncScope();
        (await Shards(check).AnyAsync(x => x.Id == shardId)).ShouldBeTrue();
    }

    // Saves the refused member through TryCompleteAsync, then checks it was undone in the change tracker: the scope's
    // next save (a valid member) passes and doesn't send the refused one again.
    private static async Task<Error> RefusedAsync(
        TestHost host,
        Member refused)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = Context(scope);
        context.Members.Add(refused);

        var result = await UnitOfWork(scope).TryCompleteAsync();

        result.IsFailure.ShouldBeTrue();
        context.ChangeTracker.Entries().ShouldBeEmpty();
        if (await Shards(scope).AnyAsync(x => x.Id == refused.ShardId))
        {
            context.Members.Add(new Member(Guid.NewGuid(), refused.ShardId, "valid", rank: 0));
            (await UnitOfWork(scope).TryCompleteAsync()).IsSuccess.ShouldBeTrue();
        }

        await using var check = host.Services.CreateAsyncScope();
        (await Context(check).Members.AnyAsync(x => x.Id == refused.Id)).ShouldBeFalse();
        return result.Errors.ShouldHaveSingleItem();
    }

    private static TestDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<TestDbContext>();

    private static DbSet<Shard> Shards(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<TestDbContext>().Shards;

    private static IUnitOfWork UnitOfWork(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
}
