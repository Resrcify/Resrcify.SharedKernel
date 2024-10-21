using System;
using System.Linq;
using FluentAssertions;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

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
            .Should()
            .BeTrue();
        result.Value
            .Should()
            .NotBeNull();
        result.Value.Value
            .Should()
            .Be(validGuid);
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
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .ContainSingle()
                .Which
                    .Should()
                    .Be(DomainErrors.CompanyId.Empty);
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
        atomicValues
            .Should()
            .ContainSingle()
                .Which
                    .Should()
                    .Be(validGuid);
    }
}