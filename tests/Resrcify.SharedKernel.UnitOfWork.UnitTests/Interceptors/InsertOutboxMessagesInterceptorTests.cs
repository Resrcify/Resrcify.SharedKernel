
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.UnitOfWork.Converters;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Interceptors;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class InsertOutboxMessagesInterceptorTests : DbSetupBase
{
    public InsertOutboxMessagesInterceptorTests()
        : base(new InsertOutboxMessagesInterceptor(new SystemTextJsonOutboxSerializer()))
    {
    }
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new DomainEventConverter() }
    };
    [Fact]
    public async Task SaveChangesAsync_ConvertsDomainEventsToOutboxMessages()
    {
        // Arrange
        var entity = new TestAggregateRoot(SocialSecurityNumber.Create(123456789), "John Doe");
        entity.PublicRaiseDomainEvent(new TestDomainEvent(Guid.NewGuid(), "Hello, World!"));
        await DbContext.Persons.AddAsync(entity);

        // Act
        await DbContext.SaveChangesAsync();

        // Assert
        var outboxMessages = await DbContext.OutboxMessages.ToListAsync();
        var testDomainEventName = typeof(TestDomainEvent).FullName;
        outboxMessages.Count.ShouldBe(1);
        outboxMessages[0].Type.ShouldBe(testDomainEventName);
    }

    [Fact]
    public async Task SaveChangesAsync_SavesAllAvailableProperties()
    {
        // Arrange
        var entity = new TestAggregateRoot(SocialSecurityNumber.Create(123456789), "John Doe");
        entity.PublicRaiseDomainEvent(new TestDomainEvent(Guid.NewGuid(), "Test message"));
        await DbContext.Persons.AddAsync(entity);

        // Act
        await DbContext.SaveChangesAsync();

        // Assert
        var outboxMessages = await DbContext.OutboxMessages.ToListAsync();
        outboxMessages.ShouldHaveSingleItem();
        var message = outboxMessages[0];
        var deserializedMessage = (TestDomainEvent?)JsonSerializer.Deserialize<IDomainEvent>(message.Content, _jsonOptions);
        deserializedMessage.ShouldNotBeNull();
        deserializedMessage.Id.ShouldNotBe(Guid.Empty);
        deserializedMessage.Message.ShouldBe("Test message");
    }

    [Fact]
    public async Task SaveChangesAsync_WritesDedupKey_WhenEventImplementsIDedupable()
    {
        // Arrange
        var entity = new TestAggregateRoot(SocialSecurityNumber.Create(123456789), "John Doe");
        entity.PublicRaiseDomainEvent(new TestDedupableDomainEvent(Guid.NewGuid(), "shard-1", "msg"));
        await DbContext.Persons.AddAsync(entity);

        // Act
        await DbContext.SaveChangesAsync();

        // Assert
        var outboxMessages = await DbContext.OutboxMessages.ToListAsync();
        outboxMessages.ShouldHaveSingleItem();
        outboxMessages[0].DedupKey.ShouldBe($"{typeof(TestDedupableDomainEvent).FullName}:shard-1");
    }

    [Fact]
    public async Task SaveChangesAsync_DedupKeyIsNull_WhenEventDoesNotImplementIDedupable()
    {
        // Arrange
        var entity = new TestAggregateRoot(SocialSecurityNumber.Create(123456789), "John Doe");
        entity.PublicRaiseDomainEvent(new TestDomainEvent(Guid.NewGuid(), "plain"));
        await DbContext.Persons.AddAsync(entity);

        // Act
        await DbContext.SaveChangesAsync();

        // Assert
        var outboxMessages = await DbContext.OutboxMessages.ToListAsync();
        outboxMessages.ShouldHaveSingleItem();
        outboxMessages[0].DedupKey.ShouldBeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_CoalescesDuplicates_WithinSameSaveChangesBatch()
    {
        // Arrange — two IDedupable events with the same key in one SaveChanges
        var entity = new TestAggregateRoot(SocialSecurityNumber.Create(123456789), "John Doe");
        entity.PublicRaiseDomainEvent(new TestDedupableDomainEvent(Guid.NewGuid(), "shard-1", "first"));
        entity.PublicRaiseDomainEvent(new TestDedupableDomainEvent(Guid.NewGuid(), "shard-1", "second"));
        await DbContext.Persons.AddAsync(entity);

        // Act
        await DbContext.SaveChangesAsync();

        // Assert — only the first one becomes a row
        var outboxMessages = await DbContext.OutboxMessages.ToListAsync();
        outboxMessages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task SaveChangesAsync_SkipsInsert_WhenUnprocessedRowWithSameKeyAlreadyExists()
    {
        // Arrange — first SaveChanges persists an outbox row
        var entity = new TestAggregateRoot(SocialSecurityNumber.Create(123456789), "John Doe");
        entity.PublicRaiseDomainEvent(new TestDedupableDomainEvent(Guid.NewGuid(), "shard-1", "first"));
        await DbContext.Persons.AddAsync(entity);
        await DbContext.SaveChangesAsync();

        var entity2 = new TestAggregateRoot(SocialSecurityNumber.Create(987654321), "Jane Doe");
        entity2.PublicRaiseDomainEvent(new TestDedupableDomainEvent(Guid.NewGuid(), "shard-1", "second"));
        await DbContext.Persons.AddAsync(entity2);

        // Act — second SaveChanges with same DedupKey
        await DbContext.SaveChangesAsync();

        // Assert — second event was coalesced, still only one row
        var outboxMessages = await DbContext.OutboxMessages.ToListAsync();
        outboxMessages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task SaveChangesAsync_AllowsInsert_WhenExistingRowWithSameKeyIsAlreadyProcessed()
    {
        // Arrange — first event, then mark its outbox row processed
        var entity = new TestAggregateRoot(SocialSecurityNumber.Create(123456789), "John Doe");
        entity.PublicRaiseDomainEvent(new TestDedupableDomainEvent(Guid.NewGuid(), "shard-1", "first"));
        await DbContext.Persons.AddAsync(entity);
        await DbContext.SaveChangesAsync();

        var firstRow = await DbContext.OutboxMessages.SingleAsync();
        firstRow.ProcessedOnUtc = DateTime.UtcNow;
        await DbContext.SaveChangesAsync();

        var entity2 = new TestAggregateRoot(SocialSecurityNumber.Create(987654321), "Jane Doe");
        entity2.PublicRaiseDomainEvent(new TestDedupableDomainEvent(Guid.NewGuid(), "shard-1", "second"));
        await DbContext.Persons.AddAsync(entity2);

        // Act
        await DbContext.SaveChangesAsync();

        // Assert — first is processed, dedup releases, second goes through
        var unprocessed = await DbContext.OutboxMessages.Where(m => m.ProcessedOnUtc == null).ToListAsync();
        unprocessed.ShouldHaveSingleItem();
        var all = await DbContext.OutboxMessages.OrderBy(m => m.OccurredOnUtc).ToListAsync();
        all.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SaveChangesAsync_DoesNotCoalesce_AcrossDifferentEventTypesWithSameKey()
    {
        // Arrange — two different IDedupable types with the same business key
        var entity = new TestAggregateRoot(SocialSecurityNumber.Create(123456789), "John Doe");
        entity.PublicRaiseDomainEvent(new TestDedupableDomainEvent(Guid.NewGuid(), "shard-1", "msg"));
        entity.PublicRaiseDomainEvent(new AnotherTestDedupableDomainEvent(Guid.NewGuid(), "shard-1"));
        await DbContext.Persons.AddAsync(entity);

        // Act
        await DbContext.SaveChangesAsync();

        // Assert — type-prefix keeps them distinct
        var outboxMessages = await DbContext.OutboxMessages.ToListAsync();
        outboxMessages.Count.ShouldBe(2);
        outboxMessages.ShouldContain(m => m.DedupKey == $"{typeof(TestDedupableDomainEvent).FullName}:shard-1");
        outboxMessages.ShouldContain(m => m.DedupKey == $"{typeof(AnotherTestDedupableDomainEvent).FullName}:shard-1");
    }

    [Fact]
    public async Task SaveChangesAsync_DoesNotCoalesce_AcrossDifferentKeys()
    {
        // Arrange — same type, different keys
        var entity = new TestAggregateRoot(SocialSecurityNumber.Create(123456789), "John Doe");
        entity.PublicRaiseDomainEvent(new TestDedupableDomainEvent(Guid.NewGuid(), "shard-1", "msg"));
        entity.PublicRaiseDomainEvent(new TestDedupableDomainEvent(Guid.NewGuid(), "shard-2", "msg"));
        await DbContext.Persons.AddAsync(entity);

        // Act
        await DbContext.SaveChangesAsync();

        // Assert — different keys, two rows
        var outboxMessages = await DbContext.OutboxMessages.ToListAsync();
        outboxMessages.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SaveChangesAsync_DelegatesPersistence_ToInjectedInsertStrategy()
    {
        // Arrange — a probe strategy that records what gets handed to it
        var probe = new RecordingInsertStrategy();
        await using var harness = new InterceptorHarness(new InsertOutboxMessagesInterceptor(new SystemTextJsonOutboxSerializer(), probe));
        await harness.InitializeAsync();

        var entity = new TestAggregateRoot(SocialSecurityNumber.Create(123456789), "John Doe");
        entity.PublicRaiseDomainEvent(new TestDomainEvent(Guid.NewGuid(), "msg"));
        await harness.DbContext.Persons.AddAsync(entity);

        // Act
        await harness.DbContext.SaveChangesAsync();

        // Assert — strategy received the messages instead of the default AddRangeAsync path
        probe.InvocationCount.ShouldBe(1);
        probe.LastBatchCount.ShouldBe(1);

        // And because the probe didn't actually persist, no rows landed in the table.
        var rows = await harness.DbContext.OutboxMessages.ToListAsync();
        rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task SaveChangesAsync_NonDedupableEventsAreUnaffectedByPreCheck()
    {
        // Arrange — mix dedupable (with a pre-existing pending row) and non-dedupable in one batch
        var entity = new TestAggregateRoot(SocialSecurityNumber.Create(123456789), "John Doe");
        entity.PublicRaiseDomainEvent(new TestDedupableDomainEvent(Guid.NewGuid(), "shard-1", "first"));
        await DbContext.Persons.AddAsync(entity);
        await DbContext.SaveChangesAsync();

        var entity2 = new TestAggregateRoot(SocialSecurityNumber.Create(987654321), "Jane Doe");
        entity2.PublicRaiseDomainEvent(new TestDedupableDomainEvent(Guid.NewGuid(), "shard-1", "should-be-skipped"));
        entity2.PublicRaiseDomainEvent(new TestDomainEvent(Guid.NewGuid(), "should-go-through"));
        await DbContext.Persons.AddAsync(entity2);

        // Act
        await DbContext.SaveChangesAsync();

        // Assert — only the non-dedupable one is added in the second batch
        var allRows = await DbContext.OutboxMessages.OrderBy(m => m.OccurredOnUtc).ToListAsync();
        allRows.Count.ShouldBe(2);
        allRows.ShouldContain(m => m.Type == typeof(TestDedupableDomainEvent).FullName);
        allRows.ShouldContain(m => m.Type == typeof(TestDomainEvent).FullName);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldTellTheObservers_OnceASaveWroteMessages()
    {
        // Arrange
        var observer = new RecordingObserver();
        await using var harness = new InterceptorHarness(new InsertOutboxMessagesInterceptor(
            new SystemTextJsonOutboxSerializer(),
            observers: [observer]));
        await harness.InitializeAsync();
        var quiet = new TestAggregateRoot(SocialSecurityNumber.Create(123456780), "Quiet");
        var evented = new TestAggregateRoot(SocialSecurityNumber.Create(123456781), "Evented");
        evented.PublicRaiseDomainEvent(new TestDomainEvent(Guid.NewGuid(), "first"));
        evented.PublicRaiseDomainEvent(new TestDomainEvent(Guid.NewGuid(), "second"));

        // Act — a save without events, then one with two.
        await harness.DbContext.Persons.AddAsync(quiet);
        await harness.DbContext.SaveChangesAsync();
        await harness.DbContext.Persons.AddAsync(evented);
        await harness.DbContext.SaveChangesAsync();

        // Assert
        observer.Saved.ShouldHaveSingleItem().ShouldBe(2);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldStand_WhenAnObserverFailsAfterTheSaveCommitted()
    {
        // The save committed before the observer ran: failing it would have the caller save again what is saved.
        await using var harness = new InterceptorHarness(new InsertOutboxMessagesInterceptor(
            new SystemTextJsonOutboxSerializer(),
            observers: [new ThrowingObserver()]));
        await harness.InitializeAsync();
        var evented = new TestAggregateRoot(SocialSecurityNumber.Create(123456782), "Evented");
        evented.PublicRaiseDomainEvent(new TestDomainEvent(Guid.NewGuid(), "saved"));
        await harness.DbContext.Persons.AddAsync(evented);

        await Should.NotThrowAsync(() => harness.DbContext.SaveChangesAsync());

        (await harness.DbContext.Set<OutboxMessage>().CountAsync()).ShouldBe(1);
        evented.GetDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldFail_WhenAnObserverFailsInsideTheCallersTransaction()
    {
        await using var harness = new InterceptorHarness(new InsertOutboxMessagesInterceptor(
            new SystemTextJsonOutboxSerializer(),
            observers: [new ThrowingObserver()]));
        await harness.InitializeAsync();
        var evented = new TestAggregateRoot(SocialSecurityNumber.Create(123456783), "Evented");
        evented.PublicRaiseDomainEvent(new TestDomainEvent(Guid.NewGuid(), "rolled back"));
        await harness.DbContext.Persons.AddAsync(evented);
        await using var transaction = await harness.DbContext.Database.BeginTransactionAsync();

        await Should.ThrowAsync<InvalidOperationException>(() => harness.DbContext.SaveChangesAsync());
    }

    private sealed class ThrowingObserver : Resrcify.SharedKernel.UnitOfWork.Abstractions.IOutboxSaveObserver
    {
        public Task MessagesSavedAsync(
            Microsoft.EntityFrameworkCore.DbContext context,
            System.Collections.Generic.IReadOnlyList<OutboxMessage> messages,
            System.Threading.CancellationToken cancellationToken)
            => throw new InvalidOperationException("The wake-up failed.");
    }

    private sealed class RecordingObserver : Resrcify.SharedKernel.UnitOfWork.Abstractions.IOutboxSaveObserver
    {
        public System.Collections.Generic.List<int> Saved { get; } = [];

        public Task MessagesSavedAsync(
            Microsoft.EntityFrameworkCore.DbContext context,
            System.Collections.Generic.IReadOnlyList<OutboxMessage> messages,
            System.Threading.CancellationToken cancellationToken)
        {
            Saved.Add(messages.Count);
            return Task.CompletedTask;
        }
    }

    private sealed class TestAggregateRoot : Person
    {
        public TestAggregateRoot(SocialSecurityNumber id, string name) : base(id, name)
        {
        }
        public void PublicRaiseDomainEvent(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
    }

    // Records every call and short-circuits persistence so the test can prove the
    // interceptor delegated to the strategy rather than the default change-tracker path.
    private sealed class RecordingInsertStrategy : Resrcify.SharedKernel.UnitOfWork.Abstractions.IOutboxInsertStrategy
    {
        public int InvocationCount { get; private set; }
        public int LastBatchCount { get; private set; }

        public Task InsertAsync(
            Microsoft.EntityFrameworkCore.DbContext context,
            System.Collections.Generic.IReadOnlyList<OutboxMessage> messages,
            System.Threading.CancellationToken cancellationToken)
        {
            InvocationCount++;
            LastBatchCount = messages.Count;
            return Task.CompletedTask;
        }
    }

    // Small harness lets a single test swap in a custom interceptor without touching
    // DbSetupBase's protected ctor flow.
    private sealed class InterceptorHarness(InsertOutboxMessagesInterceptor interceptor)
        : DbSetupBase(interceptor)
    {
    }
}