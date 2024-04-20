using System.Threading.Tasks;
using FluentAssertions;
using Resrcify.SharedKernel.ResultFramework.Shared;
using Xunit;


namespace Resrcify.SharedKernel.ResultFramework.UnitTests.Shared;

public class ResultExtensionsTests
{
    [Fact]
    public void Ensure_WithSuccessAndPassingPredicate_ShouldReturnOriginalResult()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var ensuredResult = result.Ensure(x => x > 0, Error.None);

        // Assert
        ensuredResult.Should().Be(result);
    }

    [Fact]
    public void Ensure_WithSuccessAndFailingPredicate_ShouldReturnFailureResult()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var ensuredResult = result.Ensure(x => x < 0, Error.NullValue);

        // Assert
        ensuredResult.IsSuccess.Should().BeFalse();
        ensuredResult.Errors.Should().Contain(Error.NullValue);
    }

    [Fact]
    public void Ensure_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var result = Result.Failure<int>(Error.NullValue);

        // Act
        var ensuredResult = result.Ensure(x => x > 0, Error.NullValue);

        // Assert
        ensuredResult.Should().Be(result);
    }

    [Fact]
    public void Map_WithSuccess_ShouldMapToNewResult()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var mappedResult = result.Map(x => x.ToString());

        // Assert
        mappedResult.IsSuccess.Should().BeTrue();
        mappedResult.Value.Should().Be("42");
    }

    [Fact]
    public void Map_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var result = Result.Failure<int>(Error.NullValue);

        // Act
        var mappedResult = result.Map(x => x.ToString());

        // Assert
        mappedResult.IsSuccess.Should().BeFalse();
        mappedResult.Errors.Should().Contain(Error.NullValue);
    }

    [Fact]
    public async Task Bind_WithSuccessAndPassingAsyncFunc_ShouldReturnResultFromFunc()
    {
        // Arrange
        var result = Result.Success(42);
        async Task<Result<int>> AsyncFunc(int value) => await Task.FromResult(Result.Success(value * 2));

        // Act
        var boundResult = await result.Bind(AsyncFunc);

        // Assert
        boundResult.IsSuccess.Should().BeTrue();
        boundResult.Value.Should().Be(84);
    }

    [Fact]
    public async Task Bind_WithSuccessAndFailingAsyncFunc_ShouldReturnFailureResultFromFunc()
    {
        // Arrange
        var result = Result.Success(42);

        static async Task<Result<int>> AsyncFunc(int value) => await Task.FromResult(Result.Failure<int>(Error.NullValue));

        // Act
        var boundResult = await result.Bind(AsyncFunc);

        // Assert
        boundResult.IsSuccess.Should().BeFalse();
        boundResult.Errors.Should().Contain(Error.NullValue);
    }

    [Fact]
    public async Task Bind_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var originalResult = Result.Failure<int>(Error.NullValue);

        static async Task<Result<int>> AsyncFunc(int value) => await Task.FromResult(Result.Success(value * 2));

        // Act
        var boundResult = await originalResult.Bind(AsyncFunc);

        // Assert
        boundResult.IsSuccess.Should().BeFalse();
        boundResult.IsFailure.Should().BeTrue();
        boundResult.Errors.Should().BeEquivalentTo(originalResult.Errors);
    }

    [Fact]
    public void Tap_WithSuccess_ShouldInvokeActionAndReturnOriginalResult()
    {
        // Arrange
        var result = Result.Success(42);
        var actionInvoked = false;

        // Act
        var tappedResult = result.Tap(x => actionInvoked = true);

        // Assert
        actionInvoked.Should().BeTrue();
        tappedResult.Should().Be(result);
    }

    [Fact]
    public void Tap_WithFailure_ShouldNotInvokeActionAndReturnOriginalResult()
    {
        // Arrange
        var result = Result.Failure<int>(Error.NullValue);
        var actionInvoked = false;

        // Act
        var tappedResult = result.Tap(x => actionInvoked = true);

        // Assert
        actionInvoked.Should().BeFalse();
        tappedResult.Should().Be(result);
    }

}
