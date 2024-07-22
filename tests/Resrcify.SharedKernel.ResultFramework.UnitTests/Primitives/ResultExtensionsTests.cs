using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Xunit;


namespace Resrcify.SharedKernel.ResultFramework.UnitTests.Primitives;

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
        static async Task<Result<int>> AsyncFunc(int value) => await Task.FromResult(Result.Success(value * 2));

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

    [Fact]
    public async Task Create_WithSuccess_ShouldReturnMappedResult()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success(42));
        static string func(int i) => (i * 2).ToString();

        // Act
        var createdResult = await resultTask.Create(func);

        // Assert
        createdResult.IsSuccess.Should().BeTrue();
        createdResult.Value.Should().Be("84");
    }

    [Fact]
    public async Task Create_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Failure<int>(Error.NullValue));
        static string func(int i) => (i * 2).ToString();

        // Act
        var createdResult = await resultTask.Create(func);

        // Assert
        createdResult.IsSuccess.Should().BeFalse();
        createdResult.Errors.Should().Contain(Error.NullValue);
    }

    [Fact]
    public void TryCatch_WithSuccessAndNonThrowingFunc_ShouldReturnMappedResult()
    {
        // Arrange
        var result = Result.Success(42);
        static string func(int i) => (i * 2).ToString();

        // Act
        var tryCatchResult = result.TryCatch(func, Error.NullValue);

        // Assert
        tryCatchResult.IsSuccess.Should().BeTrue();
        tryCatchResult.Value.Should().Be("84");
    }

    [Fact]
    public void TryCatch_WithSuccessAndThrowingFunc_ShouldReturnFailureResult()
    {
        // Arrange
        var result = Result.Success(42);
        static string func(int i) => throw new InvalidOperationException();

        // Act
        var tryCatchResult = result.TryCatch(func, Error.NullValue);

        // Assert
        tryCatchResult.IsSuccess.Should().BeFalse();
        tryCatchResult.Errors.Should().Contain(Error.NullValue);
    }

    [Fact]
    public void TryCatch_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var result = Result.Failure<int>(Error.NullValue);
        static string func(int i) => (i * 2).ToString();

        // Act
        var tryCatchResult = result.TryCatch(func, Error.None);

        // Assert
        tryCatchResult.IsSuccess.Should().BeFalse();
        tryCatchResult.Errors.Should().Contain(Error.NullValue);
    }

    [Fact]
    public async Task TryCatchAsync_WithSuccessAndNonThrowingFunc_ShouldReturnMappedResult()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success(42));
        static string func(int i) => (i * 2).ToString();

        // Act
        var tryCatchResult = await resultTask.TryCatch(func, Error.NullValue);

        // Assert
        tryCatchResult.IsSuccess.Should().BeTrue();
        tryCatchResult.Value.Should().Be("84");
    }

    [Fact]
    public async Task TryCatchAsync_WithSuccessAndThrowingFunc_ShouldReturnFailureResult()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success(42));
        static string func(int i) => throw new InvalidOperationException();

        // Act
        var tryCatchResult = await resultTask.TryCatch(func, Error.NullValue);

        // Assert
        tryCatchResult.IsSuccess.Should().BeFalse();
        tryCatchResult.Errors.Should().Contain(Error.NullValue);
    }

    [Fact]
    public async Task TryCatchAsync_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Failure<int>(Error.NullValue));
        static string func(int i) => (i * 2).ToString();

        // Act
        var tryCatchResult = await resultTask.TryCatch(func, Error.None);

        // Assert
        tryCatchResult.IsSuccess.Should().BeFalse();
        tryCatchResult.Errors.Should().Contain(Error.NullValue);
    }

    [Fact]
    public async Task BindAsync_WithSuccessAndPassingAsyncFunc_ShouldReturnResultFromFunc()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success(42));
        static Task<Result<string>> func(int i) => Task.FromResult(Result.Success(i.ToString()));

        // Act
        var boundResult = await resultTask.Bind(func);

        // Assert
        boundResult.IsSuccess.Should().BeTrue();
        boundResult.Value.Should().Be("42");
    }

    [Fact]
    public async Task BindAsync_WithSuccessAndFailingAsyncFunc_ShouldReturnFailureResultFromFunc()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success(42));
        static Task<Result<string>> func(int i) => Task.FromResult(Result.Failure<string>(Error.NullValue));

        // Act
        var boundResult = await resultTask.Bind(func);

        // Assert
        boundResult.IsSuccess.Should().BeFalse();
        boundResult.Errors.Should().Contain(Error.NullValue);
    }

    [Fact]
    public async Task BindAsync_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Failure<int>(Error.NullValue));
        static Task<Result<string>> func(int i) => Task.FromResult(Result.Success(i.ToString()));

        // Act
        var boundResult = await resultTask.Bind(func);

        // Assert
        boundResult.IsSuccess.Should().BeFalse();
        boundResult.Errors.Should().Contain(Error.NullValue);
    }

    [Fact]
    public void Match_WithSuccess_ShouldInvokeOnSuccess()
    {
        // Arrange
        var result = Result.Success(42);
        Func<int, string> onSuccess = x => $"Success: {x}";
        Func<Error[], string> onFailure = errors => "Failure";

        // Act
        var matchResult = result.Match(onSuccess, onFailure);

        // Assert
        matchResult.Should().Be("Success: 42");
    }

    [Fact]
    public void Match_WithFailure_ShouldInvokeOnFailure()
    {
        // Arrange
        var result = Result.Failure<int>(Error.NullValue);
        Func<int, string> onSuccess = x => $"Success: {x}";
        Func<Error[], string> onFailure = errors => $"Failure: {errors.First().Code}";

        // Act
        var matchResult = result.Match(onSuccess, onFailure);

        // Assert
        matchResult.Should().Be($"Failure: {Error.NullValue.Code}");
    }

    [Fact]
    public async Task MatchAsync_WithSuccess_ShouldInvokeOnSuccess()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success(42));
        static string onSuccess(int x) => $"Success: {x}";
        static string onFailure(Error[] errors) => "Failure";

        // Act
        var matchResult = await resultTask.Match(onSuccess, onFailure);

        // Assert
        matchResult.Should().Be("Success: 42");
    }

    [Fact]
    public async Task MatchAsync_WithFailure_ShouldInvokeOnFailure()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Failure<int>(Error.NullValue));
        static string onSuccess(int x) => $"Success: {x}";
        static string onFailure(Error[] errors) => $"Failure: {errors.First().Code}";

        // Act
        var matchResult = await resultTask.Match(onSuccess, onFailure);

        // Assert
        matchResult.Should().Be($"Failure: {Error.NullValue.Code}");
    }

}
