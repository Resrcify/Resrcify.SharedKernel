using System;
using FluentAssertions;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Xunit;

namespace Resrcify.SharedKernel.DomainDrivenDesign.UnitTests.Primitives;

public class DomainEventTests
{
    private record TestDomainEvent(Guid Id) : DomainEvent(Id);
    [Fact]
    public void DomainEvent_ShouldInitialize_WithGivenId()
    {
        // Arrange
        var expectedGuid = Guid.NewGuid();

        // Act
        var domainEvent = new TestDomainEvent(expectedGuid);

        // Assert
        domainEvent.Id.Should().Be(expectedGuid);
    }

    [Fact]
    public void DomainEvents_WithSameId_ShouldBeEqual()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var event1 = new TestDomainEvent(guid);
        var event2 = new TestDomainEvent(guid);

        // Act & Assert
        event1.Should().Be(event2);
        (event1 == event2).Should().BeTrue();
    }

    [Fact]
    public void DomainEvents_WithDifferentIds_ShouldNotBeEqual()
    {
        // Arrange
        var event1 = new TestDomainEvent(Guid.NewGuid());
        var event2 = new TestDomainEvent(Guid.NewGuid());

        // Act & Assert
        event1.Should().NotBe(event2);
        (event1 != event2).Should().BeTrue();
    }

    [Fact]
    public void DomainEvents_WhenDuplicated_ShouldHaveSameHashCode()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var event1 = new TestDomainEvent(guid);
        var event2 = new TestDomainEvent(guid);

        // Act & Assert
        event1.GetHashCode().Should().Be(event2.GetHashCode());
    }
}