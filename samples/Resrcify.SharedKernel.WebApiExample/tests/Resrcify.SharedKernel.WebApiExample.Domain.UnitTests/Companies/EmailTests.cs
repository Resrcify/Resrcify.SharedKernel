using System.Linq;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;
using Shouldly;

namespace Resrcify.SharedKernel.WebApiExample.Domain.UnitTests.Companies;

public class EmailTests
{
    [Fact]
    public void Create_ShouldCreateValidEmail_WhenValidInputIsUsed()
    {
        // Arrange
        var validEmail = "test@example.com";

        // Act
        var result = Email.Create(validEmail);

        // Assert
        result.IsSuccess
            .ShouldBeTrue();
        result.Value
            .ShouldNotBeNull();
        result.Value.Value
            .ShouldBe(validEmail);
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueIsEmpty()
    {
        // Arrange
        var emptyEmail = "";

        // Act
        var result = Email.Create(emptyEmail);

        // Assert
        result.IsFailure
            .ShouldBeTrue();
        result.Errors
            .ShouldContain(DomainErrors.Email.Empty);
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueIsTooShort()
    {
        // Arrange
        var shortEmail = "a@b";

        // Act
        var result = Email.Create(shortEmail);

        // Assert
        result.IsFailure
            .ShouldBeTrue();
        result.Errors
            .ShouldContain(DomainErrors.Email.TooShort(shortEmail, Email.MinLength));
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueIsTooLong()
    {
        // Arrange
        var longEmail = new string('a', 257) + "@example.com";

        // Act
        var result = Email.Create(longEmail);

        // Assert
        result.IsFailure
            .ShouldBeTrue();
        result.Errors
            .ShouldContain(DomainErrors.Email.TooLong(longEmail, Email.MaxLength));
    }

    [Fact]
    public void Create_ShouldReturnFailureResult_WhenValueIsInvalid()
    {
        // Arrange
        var invalidEmail = "invalid-email";

        // Act
        var result = Email.Create(invalidEmail);

        // Assert
        result.IsFailure
            .ShouldBeTrue();
        result.Errors
            .ShouldContain(DomainErrors.Email.Invalid);
    }

    [Fact]
    public void GetAtomicValues_ShouldReturnCorrectValues()
    {
        // Arrange
        var validEmail = "test@example.com";
        var email = Email.Create(validEmail).Value;

        // Act
        var atomicValues = email.GetAtomicValues().ToArray();

        // Assert
        atomicValues.ShouldHaveSingleItem();
        atomicValues[0].ShouldBe(validEmail);
    }
}