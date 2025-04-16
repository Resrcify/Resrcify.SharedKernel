using System.Collections.Generic;
using System.Linq;
using Resrcify.SharedKernel.Repository.Primitives;
using Resrcify.SharedKernel.Repository.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Repository.UnitTests.Primitives;

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
        result.ShouldNotBeNull();
        result.ShouldBeAssignableTo<IQueryable<Person>>();
        result.ShouldHaveSingleItem();
        result.Count(p => p.Name == "Bob").ShouldBe(1);
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
        result.ShouldNotBeNull();
        result.ShouldBeAssignableTo<IQueryable<Person>>();
        result.Select(x => x.Id.Value).ToList()
            .ShouldBe(result.Select(x => x.Id.Value).OrderBy(x => x).ToList());
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
        result.ShouldNotBeNull();
        result.ShouldBeAssignableTo<IQueryable<Person>>();
        result.Select(x => x.Id.Value).ToList()
            .ShouldBe(result.Select(x => x.Id.Value).OrderByDescending(_ => _).ToList());
    }
    [Fact]
    public void GetQuery_Include_ReturnsCorrectQueryable()
    {
        // Arrange
        var first = persons.FirstOrDefault();
        first!.Children.Add(new Child(SocialSecurityNumber.Create(1), first.Id));
        var data = persons.AsQueryable();
        var specification = new PersonSpecification(addInclude: x => x.Children);

        // Act
        var result = SpecificationEvaluator.GetQuery(data, specification);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeAssignableTo<IQueryable<Person>>();
        result.SelectMany(x => x.Children).ShouldHaveSingleItem();
    }
}
