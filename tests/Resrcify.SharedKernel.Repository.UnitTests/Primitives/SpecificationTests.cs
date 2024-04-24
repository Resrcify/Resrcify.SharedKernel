using System;
using System.Linq.Expressions;
using FluentAssertions;
using Resrcify.SharedKernel.GenericRepository.UnitTests.Models;
using Resrcify.SharedKernel.Repository.UnitTests.Models;
using Xunit;

namespace Resrcify.SharedKernel.Repository.UnitTests.Primitives;

public class SpecificationTests
{
    [Fact]
    public void Constructor_SetsCriteriaCorrectly()
    {
        // Arrange
        Expression<Func<Person, bool>> criteria = x => x.Id == SocialSecurityNumber.Create(1);

        // Act
        var specification = new PersonSpecification(criteria);

        // Assert
        specification.Criteria.Should().Be(criteria);
    }

    [Fact]
    public void AddInclude_AddsIncludeExpressionCorrectly()
    {
        // Arrange
        // Act
        var specification = new PersonSpecification(addInclude: x => x.Children);

        // Assert
        specification.IncludeExpressions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo((Expression<Func<Person, object>>)(x => x.Children));
    }

    [Fact]
    public void AddOrderBy_AddsOrderByExpressionCorrectly()
    {
        // Arrange
        // Act
        var specification = new PersonSpecification(criteria: null, x => x.Id);

        // Assert
        specification.OrderByExpression.Should().NotBeNull()
            .And.BeEquivalentTo((Expression<Func<Person, object>>)(x => x.Id));
    }

    [Fact]
    public void AddOrderByDescending_AddsOrderByDescendingExpressionCorrectly()
    {
        // Arrange
        // Act
        var specification = new PersonSpecification(criteria: null, orderBy: null, orderByDecending: x => x.Id);

        // Assert
        specification.OrderByDescendingExpression.Should().NotBeNull()
            .And.BeEquivalentTo((Expression<Func<Person, object>>)(x => x.Id));
    }
    [Fact]
    public void Constructor_IsSplitQuery_IsFalseByDefault()
    {
        // Arrange & Act
        var specification = new PersonSpecification();

        // Assert
        specification.IsSplitQuery.Should().BeFalse();
    }

    [Fact]
    public void Constructor_IsSplitQuery_CorrectlySetsProperty()
    {
        // Arrange & Act
        var specification = new PersonSpecification(isSplitQuery: true);

        // Assert
        specification.IsSplitQuery.Should().BeTrue();
    }

    [Fact]
    public void Constructor_IsNoTrackingQuery_IsFalseByDefault()
    {
        // Arrange & Act
        var specification = new PersonSpecification();

        // Assert
        specification.IsNoTrackingQuery.Should().BeFalse();
    }
    [Fact]
    public void Constructor_IsNoTrackingQuery_CorrectlySetsProperty()
    {
        // Arrange & Act
        var specification = new PersonSpecification(isNoTrackingQuery: true);

        // Assert
        specification.IsNoTrackingQuery.Should().BeTrue();
    }
}
