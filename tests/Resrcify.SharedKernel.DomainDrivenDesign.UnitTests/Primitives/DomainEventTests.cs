using System;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.DomainDrivenDesign.UnitTests.Primitives;

public class DomainEventTests
{
    private sealed record TestDomainEvent(Guid Id) : DomainEvent(Id);
    [Fact]
    public void DomainEvent_ShouldInitialize_WithGivenId()
    {
        // Arrange
        var expectedGuid = Guid.NewGuid();

        // Act
        var domainEvent = new TestDomainEvent(expectedGuid);

        // Assert
        domainEvent.Id.ShouldBe(expectedGuid);
    }

    [Fact]
    public void DomainEvents_WithSameId_ShouldBeEqual()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var event1 = new TestDomainEvent(guid);
        var event2 = new TestDomainEvent(guid);

        // Act & Assert
        event1.ShouldBe(event2);
        (event1 == event2).ShouldBeTrue();
    }

    [Fact]
    public void DomainEvents_WithDifferentIds_ShouldNotBeEqual()
    {
        // Arrange
        var event1 = new TestDomainEvent(Guid.NewGuid());
        var event2 = new TestDomainEvent(Guid.NewGuid());

        // Act & Assert
        event1.ShouldNotBe(event2);
        (event1 != event2).ShouldBeTrue();
    }

    [Fact]
    public void DomainEvents_WhenDuplicated_ShouldHaveSameHashCode()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var event1 = new TestDomainEvent(guid);
        var event2 = new TestDomainEvent(guid);

        // Act & Assert
        event1.GetHashCode().ShouldBe(event2.GetHashCode());
    }
}