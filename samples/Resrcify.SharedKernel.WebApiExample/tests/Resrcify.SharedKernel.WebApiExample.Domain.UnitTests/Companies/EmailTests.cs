using System.Linq;
using FluentAssertions;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.ValueObjects;
using Resrcify.SharedKernel.WebApiExample.Domain.Errors;

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
            .Should()
            .BeTrue();
        result.Value
            .Should()
            .NotBeNull();
        result.Value.Value
            .Should()
            .Be(validEmail);
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
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .Contain(DomainErrors.Email.Empty);
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
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .Contain(DomainErrors.Email.TooShort(shortEmail, Email.MinLength));
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
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .Contain(DomainErrors.Email.TooLong(longEmail, Email.MaxLength));
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
            .Should()
            .BeTrue();
        result.Errors
            .Should()
            .Contain(DomainErrors.Email.Invalid);
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
        atomicValues
            .Should()
            .ContainSingle()
                .Which
                .Should()
                .Be(validEmail);
    }
}