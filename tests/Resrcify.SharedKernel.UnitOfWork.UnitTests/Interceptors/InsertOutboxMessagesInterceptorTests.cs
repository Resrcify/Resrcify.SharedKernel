
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Interceptors;

public class InsertOutboxMessagesInterceptorTests : DbSetupBase
{
    public InsertOutboxMessagesInterceptorTests() : base(new InsertOutboxMessagesInterceptor())
    {
    }

    [Fact]
    public async Task SaveChangesAsync_ConvertsDomainEventsToOutboxMessages()
    {
        // Arrange
        var entity = new TestAggregateRoot(SocialSecurityNumber.Create(123456789), "John Doe");
        entity.PublicRaiseDomainEvent(new TestDomainEvent(Guid.NewGuid()));
        await DbContext.Persons.AddAsync(entity);

        // Act
        await DbContext.SaveChangesAsync();

        // Assert
        var outboxMessages = await DbContext.OutboxMessages.ToListAsync();
        outboxMessages.Should().HaveCount(1);
        outboxMessages.First().Type.Should().Be("Resrcify.SharedKernel.UnitOfWork.UnitTests.Models.TestDomainEvent");
    }

    private class TestAggregateRoot : Person
    {
        public TestAggregateRoot(SocialSecurityNumber id, string name) : base(id, name)
        {
        }
        public void PublicRaiseDomainEvent(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
    }
}