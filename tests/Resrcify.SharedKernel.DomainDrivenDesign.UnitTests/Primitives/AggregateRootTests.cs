using System;
using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.DomainDrivenDesign.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class AggregateRootTests
{
    private sealed class TestAggregateRoot(int id) : AggregateRoot<int>(id)
    {
        public void PublicRaiseDomainEvent(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
    }

    private sealed record TestDomainEvent(Guid Id) : DomainEvent(Id);

    [Fact]
    public void GetDomainEvents_ShouldBeEmpty_WhenNoEventsAreRaised()
    {
        // Arrange
        var aggregateRoot = new TestAggregateRoot(1);

        // Act
        var events = aggregateRoot.GetDomainEvents();

        // Assert
        events.ShouldBeEmpty();
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
        events.ShouldHaveSingleItem();
        events[0].ShouldBe(domainEvent);
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
        events.ShouldBeEmpty();
    }

    [Fact]
    public void RemoveDomainEvent_ShouldTakeBackJustThatEvent()
    {
        var aggregateRoot = new TestAggregateRoot(1);
        var kept = new TestDomainEvent(Guid.NewGuid());
        var takenBack = new TestDomainEvent(Guid.NewGuid());
        aggregateRoot.PublicRaiseDomainEvent(kept);
        aggregateRoot.PublicRaiseDomainEvent(takenBack);

        aggregateRoot.RemoveDomainEvent(takenBack);
        aggregateRoot.RemoveDomainEvent(new TestDomainEvent(Guid.NewGuid()));

        aggregateRoot.GetDomainEvents().ShouldHaveSingleItem().ShouldBe(kept);
    }
}