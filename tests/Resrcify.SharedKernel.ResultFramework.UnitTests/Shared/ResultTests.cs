using System;
using FluentAssertions;
using Resrcify.SharedKernel.ResultFramework.Shared;
using Xunit;


namespace Resrcify.SharedKernel.ResultFramework.UnitTests.Shared;

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
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Errors.Should().BeEmpty();
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
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Errors.Should().BeEquivalentTo(errors);
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
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().BeEquivalentTo(new[] { error });
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
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().BeEquivalentTo(errors);
    }

    [Fact]
    public void Constructor_WithSuccessAndNonNoneError_ShouldThrowException()
    {
        // Arrange
        bool isSuccess = true;
        Error error = Error.NullValue;

        // Act & Assert
        FluentActions.Invoking(() => new TestResult(isSuccess, error))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_WithFailureAndNoneError_ShouldThrowException()
    {
        // Arrange
        bool isSuccess = false;
        Error error = Error.None;

        // Act & Assert
        FluentActions.Invoking(() => new TestResult(isSuccess, error))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_WithSuccessAndNonEmptyErrorsArray_ShouldNotThrowException()
    {
        // Arrange
        bool isSuccess = true;
        Error[] errors = [Error.NullValue];

        // Act & Assert
        FluentActions.Invoking(() => new TestResult(isSuccess, errors))
            .Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithFailureAndEmptyErrorsArray_ShouldNotThrowException()
    {
        // Arrange
        bool isSuccess = false;
        Error[] errors = [];

        // Act & Assert
        FluentActions.Invoking(() => new TestResult(isSuccess, errors))
            .Should().NotThrow();
    }

    [Fact]
    public void Success_ShouldCreateSuccessResult()
    {
        // Act
        var result = Result.Success();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void SuccessWithValue_ShouldCreateSuccessResultWithValue()
    {
        // Arrange
        int value = 42;

        // Act
        var result = Result.Success(value);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Errors.Should().BeEmpty();
        result.Value.Should().Be(value);
    }

    [Fact]
    public void Failure_WithSingleError_ShouldCreateFailureResultWithSingleError()
    {
        // Arrange
        Error error = Error.NullValue;

        // Act
        var result = Result.Failure(error);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().BeEquivalentTo(new[] { error });
    }

    [Fact]
    public void FailureT_WithMultipleErrors_ShouldCreateFailureResultWithMultipleErrors()
    {
        // Arrange
        Error[] errors = [Error.NullValue, Error.None];

        // Act
        var result = Result.Failure(errors);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().BeEquivalentTo(errors);
    }

    [Fact]
    public void Create_WithValue_ShouldReturnSuccessResultWithProvidedValue()
    {
        // Arrange
        int value = 42;

        // Act
        var result = Result.Create(value);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Errors.Should().BeEmpty();
        result.Value.Should().Be(value);
    }

    [Fact]
    public void Create_WithNullValue_ShouldReturnFailureResultWithNullValueError()
    {
        // Act
        var result = Result.Create<int?>(null);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().BeEquivalentTo(new[] { Error.NullValue });
        result.Invoking(r => r.Value).Should().Throw<InvalidOperationException>();
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
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Errors.Should().BeEmpty();
        result.Value.Should().Be(value);
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
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().BeEquivalentTo(new[] { error });
        result.Invoking(r => r.Value).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Ensure_WithAllPredicatesReturningTrue_ShouldReturnSuccessResult()
    {
        // Arrange
        int value = 42;
        var functions = new[]
        {
            ((Func<int, bool>) (v => v == 42), Error.NullValue),
            ((Func<int, bool>) (v => v > 0), Error.NullValue),
            ((Func<int, bool>) (v => v % 2 == 0), Error.NullValue)
        };

        // Act
        var result = Result.Ensure(value, functions);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Errors.Should().BeEmpty();
        result.Value.Should().Be(value);
    }

    [Fact]
    public void Ensure_WithOnePredicateReturningFalse_ShouldReturnFailureResult()
    {
        // Arrange
        int value = 42;
        var functions = new[]
        {
            ((Func<int, bool>) (v => v == 42), Error.NullValue),
            ((Func<int, bool>) (v => v < 0), Error.NullValue),
            ((Func<int, bool>) (v => v % 2 == 0), Error.NullValue)
        };

        // Act
        var result = Result.Ensure(value, functions);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().BeEquivalentTo(new[] { Error.NullValue });
        result.Invoking(r => r.Value).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Combine_OneResult_ShouldReturnCombinedResult()
    {
        // Arrange
        var result1 = Result.Success(42);

        // Act
        var combinedResult = Result.Combine(result1);

        // Assert
        combinedResult.IsSuccess.Should().BeTrue();
        combinedResult.Value.Should().Be(42);
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
        combinedResult.IsSuccess.Should().BeTrue();
        combinedResult.Value.Should().Be((42, "hello"));
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
        combinedResult.IsSuccess.Should().BeTrue();
        combinedResult.Value.Should().Be((42, "hello", true));
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
        combinedResult.IsSuccess.Should().BeTrue();
        combinedResult.Value.Should().Be((42, "hello", true, 3.14));
    }

    [Fact]
    public void Combine_FiveResults_ShouldReturnCombinedResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");
        var result3 = Result.Success(true);
        var result4 = Result.Success(3.14);
        var fixedDate = new DateTime(2022, 1, 1);
        var result5 = Result.Success(fixedDate);

        // Act
        var combinedResult = Result.Combine(result1, result2, result3, result4, result5);

        // Assert
        combinedResult.IsSuccess.Should().BeTrue();
        combinedResult.Value.Should().Be((42, "hello", true, 3.14, fixedDate));
    }

    [Fact]
    public void Combine_SixResults_ShouldReturnCombinedResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");
        var result3 = Result.Success(true);
        var result4 = Result.Success(3.14);
        var fixedDate = new DateTime(2022, 1, 1);
        var result5 = Result.Success(fixedDate);
        var result6 = Result.Success(Guid.NewGuid());

        // Act
        var combinedResult = Result.Combine(result1, result2, result3, result4, result5, result6);

        // Assert
        combinedResult.IsSuccess.Should().BeTrue();
        combinedResult.Value.Should().Be((42, "hello", true, 3.14, fixedDate, result6.Value));
    }

    [Fact]
    public void Combine_OneResult_WithAnyFailure_ShouldReturnFailureResult()
    {
        // Arrange
        var result1 = Result.Failure<bool>(Error.NullValue);

        // Act
        var combinedResult = Result.Combine(result1);

        // Assert
        combinedResult.IsSuccess.Should().BeFalse();
        combinedResult.Errors.Should().Contain(Error.NullValue);
        combinedResult.Errors.Should().HaveCount(1);
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
        combinedResult.IsSuccess.Should().BeFalse();
        combinedResult.Errors.Should().Contain(Error.NullValue);
        combinedResult.Errors.Should().HaveCount(1);
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
        combinedResult.IsSuccess.Should().BeFalse();
        combinedResult.Errors.Should().Contain(Error.NullValue);
        combinedResult.Errors.Should().HaveCount(1);
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
        combinedResult.IsSuccess.Should().BeFalse();
        combinedResult.Errors.Should().Contain(Error.NullValue);
        combinedResult.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void Combine_FiveResults_WithAnyFailure_ShouldReturnFailureResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");
        var result3 = Result.Failure<bool>(Error.NullValue);
        var result4 = Result.Success(3.14);
        var fixedDate = new DateTime(2022, 1, 1);
        var result5 = Result.Success(fixedDate);

        // Act
        var combinedResult = Result.Combine(result1, result2, result3, result4, result5);

        // Assert
        combinedResult.IsSuccess.Should().BeFalse();
        combinedResult.Errors.Should().Contain(Error.NullValue);
        combinedResult.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void Combine_SixResults_WithAnyFailure_ShouldReturnFailureResult()
    {
        // Arrange
        var result1 = Result.Success(42);
        var result2 = Result.Success("hello");
        var result3 = Result.Failure<bool>(Error.NullValue);
        var result4 = Result.Success(3.14);
        var fixedDate = new DateTime(2022, 1, 1);
        var result5 = Result.Success(fixedDate);
        var result6 = Result.Failure<Guid>(Error.NullValue);

        // Act
        var combinedResult = Result.Combine(result1, result2, result3, result4, result5, result6);

        // Assert
        combinedResult.IsSuccess.Should().BeFalse();
        combinedResult.Errors.Should().Contain(Error.NullValue);
        combinedResult.Errors.Should().HaveCount(1);
    }

    private class TestResult : Result
    {
        public TestResult(bool isSuccess, Error error) : base(isSuccess, error)
        {
        }

        public TestResult(bool isSuccess, Error[] errors) : base(isSuccess, errors)
        {
        }
    }
}
