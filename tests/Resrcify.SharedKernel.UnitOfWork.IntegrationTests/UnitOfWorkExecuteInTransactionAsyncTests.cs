using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests;

/// <summary>
/// <c>ExecuteInTransactionAsync</c> on PostgreSQL with a retrying execution strategy: what a retry may and may not do,
/// a connection dropped mid-transaction, a savepoint's undo, its isolation level, and timeouts.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class UnitOfWorkExecuteInTransactionAsyncTests
    : IClassFixture<PostgresFixture>
{
    private static readonly Error Failed = Error.Failure("Command.Failed", "The command failed.");

    private readonly PostgresFixture _pg;

    public UnitOfWorkExecuteInTransactionAsyncTests(PostgresFixture pg)
        => _pg = pg;

    [Fact]
    public async Task ExecuteInTransactionAsync_ShouldRetryATransientFailure_WhenNothingWasTrackedBefore()
    {
        var failOnce = new FailFirstWrite();
        await using var database = await DatabaseAsync(failOnce);
        await using var scope = database.Services.CreateAsyncScope();
        var (context, unitOfWork) = Resolve(scope);
        var attempts = 0;

        var result = await unitOfWork.ExecuteInTransactionAsync(
            _ =>
            {
                attempts++;
                context.Aggregates.Add(new TestAggregate(Guid.NewGuid(), "retried"));
                return Task.FromResult(Result.Success());
            });

        result.IsSuccess.ShouldBeTrue();
        attempts.ShouldBe(2);
        (await database.NamesAsync()).ShouldBe(["retried"]);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ShouldThrowInsteadOfRetrying_WhenTheContextTrackedEntitiesBefore()
    {
        var failOnce = new FailFirstWrite();
        await using var database = await DatabaseAsync(failOnce);
        var id = await database.SeedAsync("before");
        await using var scope = database.Services.CreateAsyncScope();
        var (context, unitOfWork) = Resolve(scope);
        var loadedBefore = await context.Aggregates.SingleAsync(aggregate => aggregate.Id == id);
        failOnce.Arm();
        var attempts = 0;

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(
            _ =>
            {
                attempts++;
                loadedBefore.ChangeName("inside");
                return Task.FromResult(Result.Success());
            }));

        // Retried on a cleared change tracker, the update would be lost and the call would succeed.
        attempts.ShouldBe(1);
        thrown.InnerException.ShouldNotBeNull();
        (await database.NamesAsync()).ShouldBe(["before"]);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ShouldRetry_WhenTheConnectionDropsMidTransaction()
    {
        await using var database = await DatabaseAsync();
        await using var scope = database.Services.CreateAsyncScope();
        var (context, unitOfWork) = Resolve(scope);
        var attempts = 0;

        var result = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                attempts++;
                context.Aggregates.Add(new TestAggregate(Guid.NewGuid(), "after-drop"));
                if (attempts == 1)
                    await TerminateOwnBackendAsync(context, database.ConnectionString, token);
                return Result.Success();
            });

        // The rollback of the dead transaction fails too; that failure must not hide the transient one from the
        // retrying strategy.
        result.IsSuccess.ShouldBeTrue();
        attempts.ShouldBe(2);
        (await database.NamesAsync()).ShouldBe(["after-drop"]);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ShouldUndoTheOperationsChanges_WhenItFails_SoALaterSaveDoesNotWriteThem()
    {
        // A failed operation's changes, saved or not, used to stay tracked after the rollback: the scope's next save
        // wrote them (and the events raised) outside the transaction that refused them.
        await using var database = await DatabaseAsync();
        var seededId = await database.SeedAsync("start");
        var pendingId = await database.SeedAsync("pending");
        await using var scope = database.Services.CreateAsyncScope();
        var (context, unitOfWork) = Resolve(scope);
        var pendingBefore = await context.Aggregates.SingleAsync(a => a.Id == pendingId);
        pendingBefore.ChangeName("pending+before");
        var savedInside = new TestAggregate(Guid.NewGuid(), "saved-inside");
        var addedInside = new TestAggregate(Guid.NewGuid(), "added-inside");
        TestAggregate? changedInside = null;

        var result = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                context.Aggregates.Add(savedInside);
                await context.SaveChangesAsync(token);
                changedInside = await context.Aggregates.SingleAsync(a => a.Id == seededId, token);
                changedInside.ChangeName("changed-inside");
                context.Aggregates.Add(addedInside);
                return Result.Failure(Failed);
            });

        result.IsFailure.ShouldBeTrue();
        context.Entry(savedInside).State.ShouldBe(EntityState.Detached);
        context.Entry(addedInside).State.ShouldBe(EntityState.Detached);
        context.Entry(changedInside!).State.ShouldBe(EntityState.Unchanged);
        changedInside!.Name.ShouldBe("start");
        changedInside.GetDomainEvents().ShouldBeEmpty();
        context.Entry(pendingBefore).State.ShouldBe(EntityState.Modified);   // the caller's, kept
        await context.SaveChangesAsync();
        (await database.NamesAsync()).ShouldBe(["pending+before", "start"], ignoreOrder: true);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ShouldPutBackWhatASavepointSaved_WhenTheNestedOperationFails()
    {
        await using var database = await DatabaseAsync();
        var id = await database.SeedAsync("start");
        await using var scope = database.Services.CreateAsyncScope();
        var (context, unitOfWork) = Resolve(scope);
        var insertedInside = new TestAggregate(Guid.NewGuid(), "inserted");

        var result = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var aggregate = await context.Aggregates.SingleAsync(a => a.Id == id, token);
                var nested = await unitOfWork.ExecuteInTransactionAsync(
                    async innerToken =>
                    {
                        aggregate.ChangeName("inner");
                        context.Aggregates.Add(insertedInside);
                        await context.SaveChangesAsync(innerToken);
                        return Result.Failure(Failed);
                    },
                    cancellationToken: token);

                nested.IsFailure.ShouldBeTrue();
                aggregate.Name.ShouldBe("start");
                context.Entry(aggregate).State.ShouldBe(EntityState.Unchanged);
                context.Entry(insertedInside).State.ShouldBe(EntityState.Detached);
                aggregate.ChangeName($"{aggregate.Name}+outer");
                return Result.Success();
            });

        result.IsSuccess.ShouldBeTrue();
        (await database.NamesAsync()).ShouldBe(["start+outer"]);
    }

    [Theory]
    [InlineData(IsolationLevel.Serializable)]
    [InlineData(IsolationLevel.RepeatableRead)]
    public async Task ExecuteInTransactionAsync_ShouldThrow_WhenItAsksForAStricterLevelThanTheOpenTransaction(
        IsolationLevel requested)
    {
        await using var database = await DatabaseAsync();
        await using var scope = database.Services.CreateAsyncScope();
        var (_, unitOfWork) = Resolve(scope);
        var ran = false;

        await Should.ThrowAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(
            token => unitOfWork.ExecuteInTransactionAsync(
                _ =>
                {
                    ran = true;
                    return Task.FromResult(Result.Success());
                },
                requested,
                cancellationToken: token)));

        ran.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ShouldJoinTheOpenTransaction_WhenItAsksForTheSameLevel()
    {
        await using var database = await DatabaseAsync();
        await using var scope = database.Services.CreateAsyncScope();
        var (context, unitOfWork) = Resolve(scope);

        var result = await unitOfWork.ExecuteInTransactionAsync(
            token => unitOfWork.ExecuteInTransactionAsync(
                _ =>
                {
                    context.Aggregates.Add(new TestAggregate(Guid.NewGuid(), "joined"));
                    return Task.FromResult(Result.Success());
                },
                IsolationLevel.ReadCommitted,
                cancellationToken: token));

        result.IsSuccess.ShouldBeTrue();
        (await database.NamesAsync()).ShouldBe(["joined"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExecuteInTransactionAsync_ShouldRunWithoutATimeout_WhenTheTimeoutIsZeroOrInfinite(
        int milliseconds)
    {
        await using var database = await DatabaseAsync();
        await using var scope = database.Services.CreateAsyncScope();
        var (context, unitOfWork) = Resolve(scope);

        var result = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await context.Database.ExecuteSqlRawAsync("SELECT pg_sleep(1.5)", token);
                return Result.Success();
            },
            commandTimeout: TimeSpan.FromMilliseconds(milliseconds));

        result.IsSuccess.ShouldBeTrue();
    }

    private static (TestDbContext Context, IUnitOfWork UnitOfWork) Resolve(AsyncServiceScope scope)
        => (scope.ServiceProvider.GetRequiredService<TestDbContext>(), scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

    // Ends this context's connection from another one, as a failover or a restarted server would.
    private static async Task TerminateOwnBackendAsync(
        TestDbContext context,
        string connectionString,
        CancellationToken cancellationToken)
    {
        var pid = await context.Database
            .SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync(cancellationToken);
        await using var other = new NpgsqlConnection(connectionString);
        await other.OpenAsync(cancellationToken);
        await using var terminate = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", other);
        terminate.Parameters.AddWithValue("pid", pid);
        await terminate.ExecuteScalarAsync(cancellationToken);
    }

    private async Task<TestDatabase> DatabaseAsync(
        FailFirstWrite? failOnce = null)
    {
        var connectionString = _pg.CreateIsolatedConnectionString();
        var collection = new ServiceCollection();
        collection.AddDbContext<TestDbContext>(options =>
        {
            options
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(50), null))
                .AddInterceptors(new InsertOutboxMessagesInterceptor(new SystemTextJsonOutboxSerializer()));
            if (failOnce is not null)
                options.AddInterceptors(failOnce);
        });
        collection.AddScoped<IUnitOfWork, UnitOfWork<TestDbContext>>();
        var services = collection.BuildServiceProvider();

        await using (var scope = services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        failOnce?.Arm();
        return new TestDatabase(services, connectionString);
    }

    private sealed class TestDatabase(
        ServiceProvider services,
        string connectionString)
        : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;

        public string ConnectionString { get; } = connectionString;

        public async Task<Guid> SeedAsync(string name)
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var aggregate = new TestAggregate(Guid.NewGuid(), name);
            context.Aggregates.Add(aggregate);
            await context.SaveChangesAsync();
            return aggregate.Id;
        }

        public async Task<string[]> NamesAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            return [.. (await context.Aggregates.AsNoTracking().Select(aggregate => aggregate.Name).ToListAsync()).Order()];
        }

        public ValueTask DisposeAsync()
            => Services.DisposeAsync();
    }

    /// <summary>Fails the first write after <see cref="Arm"/> with a serialization failure (40001), which is transient.</summary>
    private sealed class FailFirstWrite : DbCommandInterceptor
    {
        private int _armed;

        public void Arm()
            => Interlocked.Exchange(ref _armed, 1);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            FailIfArmed(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            FailIfArmed(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void FailIfArmed(DbCommand command)
        {
            var writes = command.CommandText.Contains("INSERT", StringComparison.Ordinal)
                || command.CommandText.Contains("UPDATE", StringComparison.Ordinal);
            if (writes && Interlocked.Exchange(ref _armed, 0) == 1)
                throw new PostgresException("Injected serialization failure.", "ERROR", "ERROR", "40001");
        }
    }
}
