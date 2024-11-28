
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Converters;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Interceptors;

public class InsertOutboxMessagesInterceptorTests : DbSetupBase
{
    public InsertOutboxMessagesInterceptorTests() : base(new InsertOutboxMessagesInterceptor())
    {
    }
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new PolymorphicJsonConverter<IDomainEvent>() }
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
        var testDomainEventName = typeof(TestDomainEvent).AssemblyQualifiedName!;
        outboxMessages.Should().HaveCount(1);
        outboxMessages.First().Type.Should().Be(testDomainEventName);
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
        outboxMessages.Should().HaveCount(1);
        var message = outboxMessages.First();
        var deserializedMessage = (TestDomainEvent?)JsonSerializer.Deserialize(message.Content, typeof(IDomainEvent), _jsonOptions);
        deserializedMessage.Should().NotBeNull();
        deserializedMessage!.Id.Should().NotBe(Guid.Empty);
        deserializedMessage!.Message.Should().Be("Test message");
    }

    private class TestAggregateRoot : Person
    {
        public TestAggregateRoot(SocialSecurityNumber id, string name) : base(id, name)
        {
        }
        public void PublicRaiseDomainEvent(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
    }
}