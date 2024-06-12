using System;
using FluentAssertions;
using Resrcify.SharedKernel.ResultFramework.Primitives;
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
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Errors.Should().BeEmpty();
        result.Value.Should().Be(value);
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
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Errors.Should().BeEquivalentTo(errors);
        result.Value.Should().Be(value);
    }

    [Fact]
    public void ConstructorT_WithFailureAndSingleError_ShouldCreateFailureResultWithSingleError()
    {
        // Arrange
        Error error = Error.NullValue;

        // Act
        var result = new TestResult<int>(default, false, error);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().BeEquivalentTo(new[] { error });
        result.Invoking(r => r.Value).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ConstructorT_WithFailureAndMultipleErrors_ShouldCreateFailureResultWithMultipleErrors()
    {
        // Arrange
        Error[] errors = [Error.None, Error.NullValue];

        // Act
        var result = new TestResult<int>(default, false, errors);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().BeEquivalentTo(errors);
        result.Invoking(r => r.Value).Should().Throw<InvalidOperationException>();
    }

    private class TestResult<TValue> : Result<TValue>
    {
        public TestResult(TValue? value, bool isSuccess, Error error) : base(value, isSuccess, error)
        {
        }

        public TestResult(TValue? value, bool isSuccess, Error[] errors) : base(value, isSuccess, errors)
        {
        }
    }
}
