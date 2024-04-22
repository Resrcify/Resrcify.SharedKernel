using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Resrcify.SharedKernel.GenericRepository.Specifications;
using Resrcify.SharedKernel.GenericRepository.UnitTests.Models;
using Xunit;

namespace Resrcify.SharedKernel.GenericRepository.UnitTests.Specifications;

public class SpecificationEvaluatorTests
{
    private readonly List<Person> persons =
    [
        new (SocialSecurityNumber.Create(1), "Alice"),
        new (SocialSecurityNumber.Create(2), "Charlie"),
        new (SocialSecurityNumber.Create(3), "Bob")
    ];

    [Fact]
    public void GetQuery_Criteria_ReturnsCorrectQueryable()
    {
        // Arrange
        var data = persons.AsQueryable();

        var specification = new PersonSpecification(x => x.Name == "Bob");

        // Act
        var result = SpecificationEvaluator.GetQuery(data, specification);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IQueryable<Person>>();
        result.Should().HaveCount(1);
        result.Should().ContainSingle(p => p.Name == "Bob");
    }

    [Fact]
    public void GetQuery_OrderBy_ReturnsCorrectQueryable()
    {
        // Arrange
        var data = persons.OrderByDescending(x => x.Id.Value).AsQueryable();

        var specification = new PersonSpecification(orderBy: x => x.Id.Value);

        // Act
        var result = SpecificationEvaluator.GetQuery(data, specification);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IQueryable<Person>>();
        result.Should().BeInAscendingOrder(x => x.Id.Value);
    }
    [Fact]
    public void GetQuery_OrderByDescending_ReturnsCorrectQueryable()
    {
        // Arrange
        var data = persons.OrderBy(x => x.Id.Value).AsQueryable();

        var specification = new PersonSpecification(orderByDecending: x => x.Id.Value);

        // Act
        var result = SpecificationEvaluator.GetQuery(data, specification);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IQueryable<Person>>();
        result.Should().BeInDescendingOrder(x => x.Id.Value);
    }
    [Fact]
    public void GetQuery_Include_ReturnsCorrectQueryable()
    {
        // Arrange
        var first = persons.FirstOrDefault();
        first!.Children.Add(new Child(SocialSecurityNumber.Create(1)));
        var data = persons.AsQueryable();
        var specification = new PersonSpecification(addInclude: x => x.Children);

        // Act
        var result = SpecificationEvaluator.GetQuery(data, specification);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IQueryable<Person>>();
        result.SelectMany(x => x.Children).Should().HaveCount(1);
    }
}
