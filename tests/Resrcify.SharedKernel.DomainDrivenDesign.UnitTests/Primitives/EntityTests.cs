using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.DomainDrivenDesign.UnitTests.Primitives;

public class EntityTests
{
    private sealed class ConcreteEntity(int id) : Entity<int>(id)
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
        entity.Id.ShouldBe(id);
    }

    [Fact]
    public void Equals_ShouldReturnTrueForSameId()
    {
        // Arrange
        var entity1 = new ConcreteEntity(1);
        var entity2 = new ConcreteEntity(1);

        // Act & Assert
        entity1.Equals(entity2).ShouldBeTrue();
    }

    [Fact]
    public void Equals_ShouldReturnFalseForDifferentIds()
    {
        // Arrange
        var entity1 = new ConcreteEntity(1);
        var entity2 = new ConcreteEntity(2);

        // Act & Assert
        entity1.Equals(entity2).ShouldBeFalse();
    }
    [SuppressMessage(
    "Maintainability",
    "CA1508:Avoid dead conditional code",
    Justification = "Its correctly testing if method returns false for null.")]
    [Fact]
    public void Equals_ShouldReturnFalseForNull()
    {
        // Arrange
        var entity = new ConcreteEntity(1);
        Entity<int>? nullEntity = null;

        // Act & Assert
        entity.Equals(nullEntity).ShouldBeFalse();
    }

    [Fact]
    public void Operator_Equals_ShouldReturnTrueForSameId()
    {
        // Arrange
        var entity1 = new ConcreteEntity(1);
        var entity2 = new ConcreteEntity(1);

        // Act & Assert
        (entity1 == entity2).ShouldBeTrue();
    }

    [Fact]
    public void Operator_NotEquals_ShouldReturnTrueForDifferentIds()
    {
        // Arrange
        var entity1 = new ConcreteEntity(1);
        var entity2 = new ConcreteEntity(2);

        // Act & Assert
        (entity1 != entity2).ShouldBeTrue();
    }

    [Fact]
    public void GetHashCode_ShouldReturnConsistentValue()
    {
        // Arrange
        var id = 1;
        var entity = new ConcreteEntity(id);
        var expectedHashCode = id.GetHashCode() * 41;

        // Act & Assert
        entity.GetHashCode().ShouldBe(expectedHashCode);
    }

    [Fact]
    public void Operator_Equals_ShouldReturnFalseForDifferentIds()
    {
        // Arrange
        var entity1 = new ConcreteEntity(1);
        var entity2 = new ConcreteEntity(2);

        // Act & Assert
        (entity1 == entity2).ShouldBeFalse();
    }


    [Fact]
    public void Operator_NotEquals_ShouldReturnFalseForSameId()
    {
        // Arrange
        var entity1 = new ConcreteEntity(1);
        var entity2 = new ConcreteEntity(1);

        // Act & Assert
        (entity1 != entity2).ShouldBeFalse();
    }
}