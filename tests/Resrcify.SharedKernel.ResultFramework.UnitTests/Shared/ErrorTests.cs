using Xunit;
using FluentAssertions;
using Resrcify.SharedKernel.ResultFramework.Shared;

namespace Resrcify.SharedKernel.ResultFramework.UnitTests.Shared;

public class ErrorTests
{
    [Fact]
    public void None_ShouldBeStaticInstance()
    {
        // Arrange & Act
        var error = Error.None;

        // Assert
        error.Code
            .Should()
            .BeEmpty();

        error.Message
            .Should()
            .BeEmpty();

        error.Type
            .Should()
            .Be(ErrorType.Failure);
    }

    [Fact]
    public void NullValue_ShouldBeStaticInstanceWithCorrectValues()
    {
        // Arrange & Act
        var error = Error.NullValue;

        // Assert
        error.Code
            .Should()
            .Be("Error.NullValue");

        error.Message
            .Should()
            .Be("The specified result value is null.");

        error.Type
            .Should()
            .Be(ErrorType.Failure);
    }

    [Fact]
    public void ImplicitConversionToString_ShouldReturnErrorCode()
    {
        // Arrange
        var error = new Error("Code", "Message", ErrorType.Failure);

        // Act
        string errorCode = error;

        // Assert
        errorCode
            .Should()
            .Be("Code");
    }

    [Fact]
    public void ImplicitConversionToResult_ShouldReturnFailureResultWithSameError()
    {
        // Arrange
        var error = new Error("Code", "Message", ErrorType.Failure);

        // Act
        Result result = error;

        // Assert
        result.IsSuccess
            .Should()
            .BeFalse();

        result.Errors
            .Should()
            .Contain(error);
    }

    [Theory]
    [InlineData("", "Message", ErrorType.Failure)]
    [InlineData("Code", "Message1", ErrorType.Conflict)]
    [InlineData("OK", "Message", ErrorType.Validation)]
    public void Equals_ShouldReturnTrueForEqualErrors(
        string code,
        string message,
        ErrorType errorType)
    {
        // Arrange
        var error1 = new Error(code, message, errorType);
        var error2 = new Error(code, message, errorType);

        // Act & Assert
        error1
            .Equals(error2)
            .Should()
            .BeTrue();

        error1
            .Should()
            .Be(error2);
    }

    [Theory]
    [InlineData("Code1", "Message", ErrorType.Failure, "Code", "Message", ErrorType.Failure)]
    [InlineData("Code", "Message1", ErrorType.Failure, "Code", "Message", ErrorType.Failure)]
    [InlineData("Code", "Message", ErrorType.Validation, "Code", "Message", ErrorType.Failure)]
    public void Equals_ShouldReturnFalseForDifferentErrors(
        string code1,
        string message1,
        ErrorType errorType1,
        string code2,
        string message2,
        ErrorType errorType2)
    {
        // Arrange
        var error1 = new Error(code1, message1, errorType1);
        var error2 = new Error(code2, message2, errorType2);

        // Act & Assert
        error1
            .Equals(error2)
            .Should()
            .BeFalse();

        error1
            .Should()
            .NotBe(error2);
    }

    [Theory]
    [InlineData("", "Message", ErrorType.Failure)]
    [InlineData("Code", "Message1", ErrorType.Conflict)]
    [InlineData("OK", "Message", ErrorType.Validation)]
    public void GetHashCode_ShouldReturnSameValueForEqualErrors(
        string code,
        string message,
        ErrorType errorType)
    {
        // Arrange
        var error1 = new Error(code, message, errorType);
        var error2 = new Error(code, message, errorType);

        // Act & Assert
        error1
            .GetHashCode()
            .Should()
            .Be(error2.GetHashCode());
    }

    [Theory]
    [InlineData("Code1", "Message", ErrorType.Failure, "Code", "Message", ErrorType.Failure)]
    [InlineData("Code", "Message1", ErrorType.Failure, "Code", "Message", ErrorType.Failure)]
    [InlineData("Code", "Message", ErrorType.Validation, "Code", "Message", ErrorType.Failure)]
    public void GetHashCode_ShouldReturnDifferentValueForDifferentErrors(
        string code1,
        string message1,
        ErrorType errorType1,
        string code2,
        string message2,
        ErrorType errorType2)
    {
        // Arrange
        var error1 = new Error(code1, message1, errorType1);
        var error2 = new Error(code2, message2, errorType2);

        // Act & Assert
        error1
            .GetHashCode()
            .Should()
            .NotBe(error2.GetHashCode());
    }

    [Fact]
    public void ToString_ShouldReturnErrorCode()
    {
        // Arrange
        var error = new Error("Code", "Message", ErrorType.Failure);

        // Act
        string errorCode = error.ToString();

        // Assert
        errorCode
            .Should()
            .Be("Code");
    }
}