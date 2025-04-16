using Xunit;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Shouldly;

namespace Resrcify.SharedKernel.ResultFramework.UnitTests.Primitives;

public class ErrorTests
{
    [Fact]
    public void None_ShouldBeStaticInstance()
    {
        // Arrange & Act
        var error = Error.None;

        // Assert
        error.Code
            .ShouldBeEmpty();

        error.Message
            .ShouldBeEmpty();

        error.Type
            .ShouldBe(ErrorType.Failure);
    }

    [Fact]
    public void NullValue_ShouldBeStaticInstanceWithCorrectValues()
    {
        // Arrange & Act
        var error = Error.NullValue;

        // Assert
        error.Code
            .ShouldBe("Error.NullValue");

        error.Message
            .ShouldBe("The specified result value is null.");

        error.Type
            .ShouldBe(ErrorType.Failure);
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
            .ShouldBe("Code");
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
            .ShouldBeFalse();

        result.Errors
            .ShouldContain(error);
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
            .ShouldBeTrue();

        error1
            .ShouldBe(error2);
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
            .ShouldBeFalse();

        error1
            .ShouldNotBe(error2);
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
            .ShouldBe(error2.GetHashCode());
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
            .ShouldNotBe(error2.GetHashCode());
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
            .ShouldBe("Code");
    }
}