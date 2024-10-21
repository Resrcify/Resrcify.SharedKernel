using System.Linq;
using FluentAssertions;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

namespace Resrcify.SharedKernel.WebApiExample.Domain.UnitTests.Companies;

public class OrganizationNumberTests
{
    [Fact]
    public void Create_ShouldCreateValidOrganizationNumber_WhenValidInputIsUsed()
    {
        // Arrange
        var validOrganizationNumber = "5555555555";

        // Act
        var result = OrganizationNumber.Create(validOrganizationNumber);

        // Assert
        result.IsSuccess
            .Should()
            .BeTrue();
        result.Value
            .Should()
            .NotBeNull();
        result.Value.Value
            .Should()
            .Be(long.Parse(validOrganizationNumber));
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueIsEmpty()
    {
        // Arrange
        var zeroOrganizationNumber = string.Empty;

        // Act
        var result = OrganizationNumber.Create(zeroOrganizationNumber);

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .Contain(DomainErrors.OrganizationNumber.Empty);
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueHasInvalidLength()
    {
        // Arrange
        var invalidLengthOrganizationNumber = "123456";

        // Act
        var result = OrganizationNumber.Create(invalidLengthOrganizationNumber);

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .Contain(DomainErrors.OrganizationNumber.InvalidLength);
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueStartsWithInvalidDigit()
    {
        // Arrange
        var invalidStartingDigitOrganizationNumber = "0555555555";

        // Act
        var result = OrganizationNumber.Create(invalidStartingDigitOrganizationNumber);

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .Contain(DomainErrors.OrganizationNumber.InvalidStartingDigit);
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueHasInvalidChecksum()
    {
        // Arrange
        var invalidChecksumOrganizationNumber = "5555555551";

        // Act
        var result = OrganizationNumber.Create(invalidChecksumOrganizationNumber);

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .Contain(DomainErrors.OrganizationNumber.InvalidChecksum);
    }

    [Fact]
    public void GetAtomicValues_ShouldReturnCorrectValues()
    {
        // Arrange
        var validOrganizationNumber = "5555555555";
        var organizationNumber = OrganizationNumber.Create(validOrganizationNumber).Value;

        // Act
        var atomicValues = organizationNumber.GetAtomicValues().ToArray();

        // Assert
        atomicValues
            .Should()
            .ContainSingle()
                .Which
                .Should()
                .Be(long.Parse(validOrganizationNumber));
    }
}