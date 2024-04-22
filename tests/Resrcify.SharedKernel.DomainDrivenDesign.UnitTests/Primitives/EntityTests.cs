using FluentAssertions;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Xunit;

namespace Resrcify.SharedKernel.DomainDrivenDesign.UnitTests.Primitives;

public class EntityTests
{
    private class ConcreteEntity(int id) : Entity<int>(id)
    {
    }

    [Fact]
    public void Constructor_ShouldInitializeId()
    {
        // Arrange
        var id = 5;

        // Act
        var entity = new ConcreteEntity(id);

        // Assert
        entity.Id.Should().Be(id);
    }

    [Fact]
    public void Equals_ShouldReturnTrueForSameId()
    {
        // Arrange
        var entity1 = new ConcreteEntity(1);
        var entity2 = new ConcreteEntity(1);

        // Act & Assert
        entity1.Equals(entity2).Should().BeTrue();
    }

    [Fact]
    public void Equals_ShouldReturnFalseForDifferentIds()
    {
        // Arrange
        var entity1 = new ConcreteEntity(1);
        var entity2 = new ConcreteEntity(2);

        // Act & Assert
        entity1.Equals(entity2).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnFalseForNull()
    {
        // Arrange
        var entity = new ConcreteEntity(1);
        Entity<int>? nullEntity = null;

        // Act & Assert
        entity.Equals(nullEntity).Should().BeFalse();
    }

    [Fact]
    public void Operator_Equals_ShouldReturnTrueForSameId()
    {
        // Arrange
        var entity1 = new ConcreteEntity(1);
        var entity2 = new ConcreteEntity(1);

        // Act & Assert
        (entity1 == entity2).Should().BeTrue();
    }

    [Fact]
    public void Operator_NotEquals_ShouldReturnTrueForDifferentIds()
    {
        // Arrange
        var entity1 = new ConcreteEntity(1);
        var entity2 = new ConcreteEntity(2);

        // Act & Assert
        (entity1 != entity2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_ShouldReturnConsistentValue()
    {
        // Arrange
        var id = 1;
        var entity = new ConcreteEntity(id);
        var expectedHashCode = id.GetHashCode() * 41;

        // Act & Assert
        entity.GetHashCode().Should().Be(expectedHashCode);
    }

    [Fact]
    public void Operator_Equals_ShouldReturnFalseForDifferentIds()
    {
        // Arrange
        var entity1 = new ConcreteEntity(1);
        var entity2 = new ConcreteEntity(2);

        // Act & Assert
        (entity1 == entity2).Should().BeFalse();
    }


    [Fact]
    public void Operator_NotEquals_ShouldReturnFalseForSameId()
    {
        // Arrange
        var entity1 = new ConcreteEntity(1);
        var entity2 = new ConcreteEntity(1);

        // Act & Assert
        (entity1 != entity2).Should().BeFalse();
    }
}