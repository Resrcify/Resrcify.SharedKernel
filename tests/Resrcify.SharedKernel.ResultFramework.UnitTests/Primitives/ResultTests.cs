using System;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Shouldly;
using Xunit;


namespace Resrcify.SharedKernel.ResultFramework.UnitTests.Primitives;

public class ResultTests
{
    [Fact]
    public void Constructor_WithSuccessAndSingleError_ShouldCreateSuccessResult()
    {
        // Arrange
        bool isSuccess = true;
        Error error = Error.None;

        // Act
        var result = new TestResult(isSuccess, error);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Constructor_WithSuccessAndMultipleErrors_ShouldCreateSuccessResult()
    {
        // Arrange
        bool isSuccess = true;
        Error[] errors = [Error.None, Error.NullValue];

        // Act
        var result = new TestResult(isSuccess, errors);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Errors.ShouldBeEquivalentTo(errors);
    }

    [Fact]
    public void Constructor_WithFailureAndSingleError_ShouldCreateFailureResult()
    {
        // Arrange
        bool isSuccess = false;
        Error error = Error.NullValue;

        // Act
        var result = new TestResult(isSuccess, error);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([error]);
    }

    [Fact]
    public void Constructor_WithFailureAndMultipleErrors_ShouldCreateFailureResult()
    {
        // Arrange
        bool isSuccess = false;
        Error[] errors = [Error.None, Error.NullValue];

        // Act
        var result = new TestResult(isSuccess, errors);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBeEquivalentTo(errors);
    }

    [Fact]
    public void Constructor_WithSuccessAndNonNoneError_ShouldThrowException()
    {
        // Arrange
        bool isSuccess = true;
        Error error = Error.NullValue;

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => new TestResult(isSuccess, error));
    }

    [Fact]
    public void Constructor_WithFailureAndNoneError_ShouldThrowException()
    {
        // Arrange
        bool isSuccess = false;
        Error error = Error.None;

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => new TestResult(isSuccess, error));
    }

    [Fact]
    public void Constructor_WithSuccessAndNonEmptyErrorsArray_ShouldNotThrowException()
    {
        // Arrange
        bool isSuccess = true;
        Error[] errors = [Error.NullValue];

        // Act & Assert
        Should.NotThrow(() => new TestResult(isSuccess, errors));
    }

    [Fact]
    public void Constructor_WithFailureAndEmptyErrorsArray_ShouldNotThrowException()
    {
        // Arrange
        bool isSuccess = false;
        Error[] errors = [];

        // Act & Assert
        Should.NotThrow(() => new TestResult(isSuccess, errors));
    }

    [Fact]
    public void Success_ShouldCreateSuccessResult()
    {
        // Act
        var result = Result.Success();

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void SuccessWithValue_ShouldCreateSuccessResultWithValue()
    {
        // Arrange
        int value = 42;

        // Act
        var result = Result.Success(value);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Errors.ShouldBeEmpty();
        result.Value.ShouldBe(value);
    }

    [Fact]
    public void Failure_WithSingleError_ShouldCreateFailureResultWithSingleError()
    {
        // Arrange
        Error error = Error.NullValue;

        // Act
        var result = Result.Failure(error);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([error]);
    }

    [Fact]
    public void FailureT_WithMultipleErrors_ShouldCreateFailureResultWithMultipleErrors()
    {
        // Arrange
        Error[] errors = [Error.NullValue, Error.None];

        // Act
        var result = Result.Failure(errors);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBeEquivalentTo(errors);
    }

    [Fact]
    public void Create_WithValue_ShouldReturnSuccessResultWithProvidedValue()
    {
        // Arrange
        int value = 42;

        // Act
        var result = Result.Create(value);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Errors.ShouldBeEmpty();
        result.Value.ShouldBe(value);
    }

    [Fact]
    public void Create_WithNullValue_ShouldReturnFailureResultWithNullValueError()
    {
        // Act
        var result = Result.Create<int?>(null);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([Error.NullValue]);
        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Ensure_WithPredicateReturningTrue_ShouldReturnSuccessResult()
    {
        // Arrange
        int value = 42;
        static bool predicate(int v) => v == 42;
        Error error = Error.NullValue;

        // Act
        var result = Result.Ensure(value, predicate, error);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Errors.ShouldBeEmpty();
        result.Value.ShouldBe(value);
    }

    [Fact]
    public void Ensure_WithPredicateReturningFalse_ShouldReturnFailureResult()
    {
        // Arrange
        int value = 42;
        static bool predicate(int v) => v != 42;
        Error error = Error.NullValue;

        // Act
        var result = Result.Ensure(value, predicate, error);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([error]);
        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Ensure_WithAllPredicatesReturningTrue_ShouldReturnSuccessResult()
    {
        // Arrange
        int value = 42;
        var functions = new[]
        {
            ( v => v == 42, Error.NullValue),
            ( v => v > 0, Error.NullValue),
            ((Func<int, bool>) (v => v % 2 == 0), Error.NullValue)
        };

        // Act
        var result = Result.Ensure(value, functions);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Errors.ShouldBeEmpty();
        result.Value.ShouldBe(value);
    }

    [Fact]
    public void Ensure_WithOnePredicateReturningFalse_ShouldReturnFailureResult()
    {
        // Arrange
        int value = 42;
        var functions = new[]
        {
            ( v => v == 42, Error.NullValue),
            ( v => v < 0, Error.NullValue),
            ((Func<int, bool>) (v => v % 2 == 0), Error.NullValue)
        };

        // Act
        var result = Result.Ensure(value, functions);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([Error.NullValue]);
        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Combine_OneResult_ShouldReturnCombinedResult()
    {
        // Arrange
        var result1 = Result.Success(42);

        // Act
        var combinedResult = Result.Combine(result1);

        // Assert
        combinedResult.IsSuccess.ShouldBeTrue();
        combinedResult.Value.ShouldBe(42);
    }

    [Fact]
    public void Combine_TwoResults_ShouldReturnCombinedResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");

        // Act
        var combinedResult = Result.Combine(result1, result2);

        // Assert
        combinedResult.IsSuccess.ShouldBeTrue();
        combinedResult.Value.ShouldBe((42, "hello"));
    }

    [Fact]
    public void Combine_ThreeResults_ShouldReturnCombinedResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");
        var result3 = Result.Success(true);

        // Act
        var combinedResult = Result.Combine(result1, result2, result3);

        // Assert
        combinedResult.IsSuccess.ShouldBeTrue();
        combinedResult.Value.ShouldBe((42, "hello", true));
    }

    [Fact]
    public void Combine_FourResults_ShouldReturnCombinedResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");
        var result3 = Result.Success(true);
        var result4 = Result.Success(3.14);

        // Act
        var combinedResult = Result.Combine(result1, result2, result3, result4);

        // Assert
        combinedResult.IsSuccess.ShouldBeTrue();
        combinedResult.Value.ShouldBe((42, "hello", true, 3.14));
    }

    [Fact]
    public void Combine_FiveResults_ShouldReturnCombinedResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");
        var result3 = Result.Success(true);
        var result4 = Result.Success(3.14);
        var fixedDate = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result5 = Result.Success(fixedDate);

        // Act
        var combinedResult = Result.Combine(result1, result2, result3, result4, result5);

        // Assert
        combinedResult.IsSuccess.ShouldBeTrue();
        combinedResult.Value.ShouldBe((42, "hello", true, 3.14, fixedDate));
    }

    [Fact]
    public void Combine_SixResults_ShouldReturnCombinedResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");
        var result3 = Result.Success(true);
        var result4 = Result.Success(3.14);
        var fixedDate = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result5 = Result.Success(fixedDate);
        var result6 = Result.Success(Guid.NewGuid());

        // Act
        var combinedResult = Result.Combine(result1, result2, result3, result4, result5, result6);

        // Assert
        combinedResult.IsSuccess.ShouldBeTrue();
        combinedResult.Value.ShouldBe((42, "hello", true, 3.14, fixedDate, result6.Value));
    }

    [Fact]
    public void Combine_OneResult_WithAnyFailure_ShouldReturnFailureResult()
    {
        // Arrange
        var result1 = Result.Failure<bool>(Error.NullValue);

        // Act
        var combinedResult = Result.Combine(result1);

        // Assert
        combinedResult.IsSuccess.ShouldBeFalse();
        combinedResult.Errors.ShouldContain(Error.NullValue);
        combinedResult.Errors.ShouldHaveSingleItem();
    }
    [Fact]
    public void Combine_TwoResults_WithAnyFailure_ShouldReturnFailureResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Failure<bool>(Error.NullValue);

        // Act
        var combinedResult = Result.Combine(result1, result2);

        // Assert
        combinedResult.IsSuccess.ShouldBeFalse();
        combinedResult.Errors.ShouldContain(Error.NullValue);
        combinedResult.Errors.ShouldHaveSingleItem();
    }
    [Fact]
    public void Combine_ThreeResults_WithAnyFailure_ShouldReturnFailureResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");
        var result3 = Result.Failure<bool>(Error.NullValue);

        // Act
        var combinedResult = Result.Combine(result1, result2, result3);

        // Assert
        combinedResult.IsSuccess.ShouldBeFalse();
        combinedResult.Errors.ShouldContain(Error.NullValue);
        combinedResult.Errors.ShouldHaveSingleItem();
    }
    [Fact]
    public void Combine_FourResults_WithAnyFailure_ShouldReturnFailureResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");
        var result3 = Result.Failure<bool>(Error.NullValue);
        var result4 = Result.Success(3.14);

        // Act
        var combinedResult = Result.Combine(result1, result2, result3, result4);

        // Assert
        combinedResult.IsSuccess.ShouldBeFalse();
        combinedResult.Errors.ShouldContain(Error.NullValue);
        combinedResult.Errors.ShouldHaveSingleItem();
    }

    [Fact]
    public void Combine_FiveResults_WithAnyFailure_ShouldReturnFailureResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");
        var result3 = Result.Failure<bool>(Error.NullValue);
        var result4 = Result.Success(3.14);
        var fixedDate = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result5 = Result.Success(fixedDate);

        // Act
        var combinedResult = Result.Combine(result1, result2, result3, result4, result5);

        // Assert
        combinedResult.IsSuccess.ShouldBeFalse();
        combinedResult.Errors.ShouldContain(Error.NullValue);
        combinedResult.Errors.ShouldHaveSingleItem();
    }

    [Fact]
    public void Combine_SixResults_WithAnyFailure_ShouldReturnFailureResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");
        var result3 = Result.Failure<bool>(Error.NullValue);
        var result4 = Result.Success(3.14);
        var fixedDate = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result5 = Result.Success(fixedDate);
        var result6 = Result.Failure<Guid>(Error.NullValue);

        // Act
        var combinedResult = Result.Combine(result1, result2, result3, result4, result5, result6);

        // Assert
        combinedResult.IsSuccess.ShouldBeFalse();
        combinedResult.Errors.ShouldContain(Error.NullValue);
        combinedResult.Errors.ShouldHaveSingleItem();
    }

    private sealed class TestResult : Result
    {
        public TestResult(bool isSuccess, Error error) : base(isSuccess, error)
        {
        }

        public TestResult(bool isSuccess, Error[] errors) : base(isSuccess, errors)
        {
        }
    }
}
