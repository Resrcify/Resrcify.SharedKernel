using System;
using System.Linq;
using FluentAssertions;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Xunit;

namespace Resrcify.SharedKernel.DomainDrivenDesign.UnitTests.Primitives;

public class AggregateRootTests
{
    private class TestAggregateRoot(int id) : AggregateRoot<int>(id)
    {
        public void PublicRaiseDomainEvent(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
    }

    private record TestDomainEvent(Guid Id) : DomainEvent(Id);

    [Fact]
    public void GetDomainEvents_ShouldBeEmpty_WhenNoEventsAreRaised()
    {
        // Arrange
        var aggregateRoot = new TestAggregateRoot(1);

        // Act
        var events = aggregateRoot.GetDomainEvents();

        // Assert
        events.Should().BeEmpty();
    }

    [Fact]
    public void RaiseDomainEvent_ShouldAddEventToDomainEvents()
    {
        // Arrange
        var aggregateRoot = new TestAggregateRoot(1);
        var domainEvent = new TestDomainEvent(Guid.NewGuid());

        // Act
        aggregateRoot.PublicRaiseDomainEvent(domainEvent);
        var events = aggregateRoot.GetDomainEvents();

        // Assert
        events.Should().ContainSingle();
        events.First().Should().Be(domainEvent);
    }

    [Fact]
    public void ClearDomainEvents_ShouldRemoveAllDomainEvents()
    {
        // Arrange
        var aggregateRoot = new TestAggregateRoot(1);
        var domainEvent = new TestDomainEvent(Guid.NewGuid());
        aggregateRoot.PublicRaiseDomainEvent(domainEvent);

        // Act
        aggregateRoot.ClearDomainEvents();
        var events = aggregateRoot.GetDomainEvents();

        // Assert
        events.Should().BeEmpty();
    }
}