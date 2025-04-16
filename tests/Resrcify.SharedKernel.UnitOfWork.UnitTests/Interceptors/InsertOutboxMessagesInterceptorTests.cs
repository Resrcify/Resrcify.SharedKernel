
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Converters;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Interceptors;

public sealed class InsertOutboxMessagesInterceptorTests : DbSetupBase
{
    public InsertOutboxMessagesInterceptorTests() : base(new InsertOutboxMessagesInterceptor())
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

    private sealed class TestAggregateRoot : Person
    {
        public TestAggregateRoot(SocialSecurityNumber id, string name) : base(id, name)
        {
        }
        public void PublicRaiseDomainEvent(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
    }
}