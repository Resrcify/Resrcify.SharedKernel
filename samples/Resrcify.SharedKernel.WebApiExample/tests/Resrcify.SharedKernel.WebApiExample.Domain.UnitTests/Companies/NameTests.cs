using System.Linq;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;
using Shouldly;

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
            .ShouldBeTrue();
        result.Value
            .ShouldNotBeNull();
        result.Value.Value
            .ShouldBe(validName);
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
            .ShouldBeTrue();
        result.Errors
            .ShouldContain(DomainErrors.Name.Empty);
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
            .ShouldBeTrue();
        result.Errors
            .ShouldContain(DomainErrors.Name.TooShort(shortName, Name.MinLength));
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
            .ShouldBeTrue();
        result.Errors
            .ShouldContain(DomainErrors.Name.TooLong(longName, Name.MaxLength));
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
            .ShouldBeTrue();
        result.Errors
            .ShouldContain(DomainErrors.Name.Invalid);
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
        atomicValues.ShouldHaveSingleItem();
        atomicValues[0].ShouldBe(validName);
    }
}