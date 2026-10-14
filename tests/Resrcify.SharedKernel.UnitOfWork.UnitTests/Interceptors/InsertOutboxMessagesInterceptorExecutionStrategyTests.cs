using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Primitives;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Interceptors;

/// <summary>
/// The outbox interceptor under a retrying execution strategy, with an insert strategy that writes outside the save (as
/// the Postgres ON CONFLICT one does) and so needs a transaction of its own around the save.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class InsertOutboxMessagesInterceptorExecutionStrategyTests
    : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TestDbContext _context;

    public InsertOutboxMessagesInterceptorExecutionStrategyTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection, sqlite => sqlite.ExecutionStrategy(dependencies => new RetryingStrategy(dependencies)))
            .AddInterceptors(new InsertOutboxMessagesInterceptor(new SystemTextJsonOutboxSerializer(), new OutsideTheSaveInsertStrategy()))
            .Options;
        _context = new TestDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await _context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
        => Task.CompletedTask;

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldSayToSaveThroughTheUnitOfWork_WhenCalledDirectlyWithDomainEvents()
    {
        var person = RenamedPerson();
        _context.Persons.Add(person);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => _context.SaveChangesAsync());

        thrown.Message.ShouldContain("TestDbContext.SaveChangesAsync() was called directly");
        thrown.Message.ShouldContain("retrying execution strategy");
        thrown.Message.ShouldContain("Save through IUnitOfWork.CompleteAsync");
        thrown.Message.ShouldContain("CreateExecutionStrategy().ExecuteAsync(");
        // Refused before anything was written or begun, and the events kept for the save that follows.
        _context.Database.CurrentTransaction.ShouldBeNull();
        person.GetDomainEvents().ShouldHaveSingleItem();
        (await CountAsync<OutboxMessage>()).ShouldBe(0);
    }

    [Fact]
    public async Task CompleteAsync_ShouldSaveTheAggregateAndItsMessages_UnderTheRetryingStrategy()
    {
        var person = RenamedPerson();
        _context.Persons.Add(person);

        using var unitOfWork = new UnitOfWork<TestDbContext>(_context);
        await unitOfWork.CompleteAsync();

        (await CountAsync<Person>()).ShouldBe(1);
        (await CountAsync<OutboxMessage>()).ShouldBe(1);
        person.GetDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldSave_WhenRunUnderTheExecutionStrategy()
    {
        _context.Persons.Add(RenamedPerson());

        await _context.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(token => _context.SaveChangesAsync(token), CancellationToken.None);

        (await CountAsync<OutboxMessage>()).ShouldBe(1);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldStillSaveDirectly_WhenThereAreNoDomainEvents()
    {
        _context.Persons.Add(new Person(SocialSecurityNumber.Create(123456789)));

        await _context.SaveChangesAsync();

        (await CountAsync<Person>()).ShouldBe(1);
    }

    private static Person RenamedPerson()
    {
        var person = new Person(SocialSecurityNumber.Create(123456789));
        person.Rename("Renamed");
        return person;
    }

    private async Task<int> CountAsync<TEntity>()
        where TEntity : class
    {
        await using var reader = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options);
        return await reader.Set<TEntity>().CountAsync();
    }

    /// <summary>A strategy that retries (as Npgsql's does with <c>EnableRetryOnFailure</c>), though nothing here fails.</summary>
    private sealed class RetryingStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(10))
    {
        protected override bool ShouldRetryOn(Exception exception)
            => false;
    }

    /// <summary>Writes the messages itself, as the Postgres ON CONFLICT strategy does, so the save needs a transaction.</summary>
    private sealed class OutsideTheSaveInsertStrategy : IOutboxInsertStrategy
    {
        public async Task InsertAsync(
            DbContext context,
            IReadOnlyList<OutboxMessage> messages,
            CancellationToken cancellationToken)
        {
            foreach (var message in messages)
                await context.Database.ExecuteSqlAsync(
                    $"""INSERT INTO "OutboxMessages" ("Id", "Type", "Content", "OccurredOnUtc", "RetryCount") VALUES ({message.Id}, {message.Type}, {message.Content}, {message.OccurredOnUtc}, 0)""",
                    cancellationToken);
        }
    }
}
