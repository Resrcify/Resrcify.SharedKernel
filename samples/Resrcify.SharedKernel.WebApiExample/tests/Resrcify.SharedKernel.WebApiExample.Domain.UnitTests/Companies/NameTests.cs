using System.Linq;
using FluentAssertions;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

namespace Resrcify.SharedKernel.WebApiExample.Domain.UnitTests.Companies;

public class NameTests
{
    [Fact]
    public void Create_ShouldCreateValidName_WhenValidInputIsUsed()
    {
        // Arrange
        var validName = "JohnDoe";

        // Act
        var result = Name.Create(validName);

        // Assert
        result.IsSuccess
            .Should()
            .BeTrue();
        result.Value
            .Should()
            .NotBeNull();
        result.Value.Value
            .Should()
            .Be(validName);
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueIsEmpty()
    {
        // Arrange
        var emptyName = string.Empty;

        // Act
        var result = Name.Create(emptyName);

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .Contain(DomainErrors.Name.Empty);
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueIsTooShort()
    {
        // Arrange
        var shortName = string.Empty;

        // Act
        var result = Name.Create(shortName);

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .Contain(DomainErrors.Name.TooShort(shortName, Name.MinLength));
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueIsTooLong()
    {
        // Arrange
        var longName = new string('a', 101);

        // Act
        var result = Name.Create(longName);

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .Contain(DomainErrors.Name.TooLong(longName, Name.MaxLength));
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueContainsInvalidCharacters()
    {
        // Arrange
        var invalidName = "John!Doe";

        // Act
        var result = Name.Create(invalidName);

        // Assert
        result.IsFailure
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .Contain(DomainErrors.Name.Invalid);
    }

    [Fact]
    public void GetAtomicValues_ShouldReturnCorrectValues()
    {
        // Arrange
        var validName = "JohnDoe";
        var name = Name.Create(validName).Value;

        // Act
        var atomicValues = name.GetAtomicValues().ToArray();

        // Assert
        atomicValues
            .Should()
            .ContainSingle()
                .Which
                .Should()
                .Be(validName);
    }
}