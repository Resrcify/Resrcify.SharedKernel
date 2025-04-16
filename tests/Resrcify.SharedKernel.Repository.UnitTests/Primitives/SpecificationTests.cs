using System;
using System.Linq.Expressions;
using Resrcify.SharedKernel.Repository.UnitTests.Models;
using Shouldly;
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
        specification.Criteria.ShouldBe(criteria);
    }

    [Fact]
    public void AddInclude_AddsIncludeExpressionCorrectly()
    {
        // Arrange
        // Act
        var specification = new PersonSpecification(addInclude: x => x.Children);

        // Assert
        var include = specification.IncludeExpressions.ShouldHaveSingleItem();
        include.ToString().ShouldBe(((Expression<Func<Person, object>>)(x => x.Children)).ToString());
    }

    [Fact]
    public void AddOrderBy_AddsOrderByExpressionCorrectly()
    {
        // Arrange
        // Act
        var specification = new PersonSpecification(criteria: null, x => x.Id);

        // Assert
        specification.OrderByExpression.ShouldNotBeNull();
        specification.OrderByExpression.ToString().ShouldBe(((Expression<Func<Person, object>>)(x => x.Id)).ToString());
    }

    [Fact]
    public void AddOrderByDescending_AddsOrderByDescendingExpressionCorrectly()
    {
        // Arrange
        // Act
        var specification = new PersonSpecification(criteria: null, orderBy: null, orderByDecending: x => x.Id);

        // Assert
        specification.OrderByDescendingExpression.ShouldNotBeNull();
        specification.OrderByDescendingExpression.ToString()
            .ShouldBe(((Expression<Func<Person, object>>)(x => x.Id)).ToString());
    }
    [Fact]
    public void Constructor_IsSplitQuery_IsFalseByDefault()
    {
        // Arrange & Act
        var specification = new PersonSpecification();

        // Assert
        specification.IsSplitQuery.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_IsSplitQuery_CorrectlySetsProperty()
    {
        // Arrange & Act
        var specification = new PersonSpecification(isSplitQuery: true);

        // Assert
        specification.IsSplitQuery.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_IsNoTrackingQuery_IsFalseByDefault()
    {
        // Arrange & Act
        var specification = new PersonSpecification();

        // Assert
        specification.IsNoTrackingQuery.ShouldBeFalse();
    }
    [Fact]
    public void Constructor_IsNoTrackingQuery_CorrectlySetsProperty()
    {
        // Arrange & Act
        var specification = new PersonSpecification(isNoTrackingQuery: true);

        // Assert
        specification.IsNoTrackingQuery.ShouldBeTrue();
    }
}
