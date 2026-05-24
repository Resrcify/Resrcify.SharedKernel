
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
        deserializedMessage!.Id.ShouldNotBe(Guid.Empty);
        deserializedMessage!.Message.ShouldBe("Test message");
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

    private sealed class TestAggregateRoot : Person
    {
        public TestAggregateRoot(SocialSecurityNumber id, string name) : base(id, name)
        {
        }
        public void PublicRaiseDomainEvent(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
    }
}