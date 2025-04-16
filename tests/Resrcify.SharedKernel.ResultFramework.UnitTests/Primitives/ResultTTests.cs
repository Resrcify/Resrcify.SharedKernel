using System;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Shouldly;
using Xunit;


namespace Resrcify.SharedKernel.ResultFramework.UnitTests.Primitives;

public class ResultTTests
{
    [Fact]
    public void ConstructorT_WithSuccessAndSingleError_ShouldCreateSuccessResultWithSingleError()
    {
        // Arrange
        int value = 42;
        Error error = Error.None;

        // Act
        var result = new TestResult<int>(value, true, error);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Errors.ShouldBeEmpty();
        result.Value.ShouldBe(value);
    }

    [Fact]
    public void ConstructorT_WithSuccessAndMultipleErrors_ShouldCreateSuccessResultWithMultipleErrors()
    {
        // Arrange
        int value = 42;
        Error[] errors = [Error.None, Error.NullValue];

        // Act
        var result = new TestResult<int>(value, true, errors);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Errors.ShouldBeEquivalentTo(errors);
        result.Value.ShouldBe(value);
    }

    [Fact]
    public void ConstructorT_WithFailureAndSingleError_ShouldCreateFailureResultWithSingleError()
    {
        // Arrange
        Error error = Error.NullValue;

        // Act
        var result = new TestResult<int>(default, false, error);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([error]);
        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void ConstructorT_WithFailureAndMultipleErrors_ShouldCreateFailureResultWithMultipleErrors()
    {
        // Arrange
        Error[] errors = [Error.None, Error.NullValue];

        // Act
        var result = new TestResult<int>(default, false, errors);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBeEquivalentTo(errors);
        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    private sealed class TestResult<TValue> : Result<TValue>
    {
        public TestResult(TValue? value, bool isSuccess, Error error) : base(value, isSuccess, error)
        {
        }

        public TestResult(TValue? value, bool isSuccess, Error[] errors) : base(value, isSuccess, errors)
        {
        }
    }
}
