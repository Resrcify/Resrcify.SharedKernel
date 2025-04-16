using System;
using System.Linq;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;
using Shouldly;

namespace Resrcify.SharedKernel.WebApiExample.Domain.UnitTests.Companies;

public class CompanyIdTests
{
    [Fact]
    public void Create_ShouldCreateValidGuid_WhenValidInputIsUsed()
    {
        // Arrange
        var validGuid = Guid.NewGuid();

        // Act
        var result = CompanyId.Create(validGuid);

        // Assert
        result.IsSuccess
            .ShouldBeTrue();
        result.Value
            .ShouldNotBeNull();
        result.Value.Value
            .ShouldBe(validGuid);
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueIsEmpty()
    {
        // Arrange
        var emptyGuid = Guid.Empty;

        // Act
        var result = CompanyId.Create(emptyGuid);

        // Assert
        result.IsFailure
            .ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem();

        // Assert that the error is equal to DomainErrors.CompanyId.Empty
        result.Errors[0].ShouldBe(DomainErrors.CompanyId.Empty);
    }

    [Fact]
    public void GetAtomicValues_ShouldReturnCorrectValues()
    {
        // Arrange
        var validGuid = Guid.NewGuid();
        var companyId = CompanyId.Create(validGuid).Value;

        // Act
        var atomicValues = companyId.GetAtomicValues().ToArray();

        // Assert
        atomicValues.ShouldHaveSingleItem();
        atomicValues[0].ShouldBe(validGuid);
    }
}