using System;
using System.Globalization;
using System.Threading.Tasks;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Shouldly;
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
        ensuredResult.ShouldBe(result);
    }

    [Fact]
    public void Ensure_WithSuccessAndFailingPredicate_ShouldReturnFailureResult()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var ensuredResult = result.Ensure(x => x < 0, Error.NullValue);

        // Assert
        ensuredResult.IsSuccess.ShouldBeFalse();
        ensuredResult.Errors.ShouldContain(Error.NullValue);
    }

    [Fact]
    public void Ensure_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var result = Result.Failure<int>(Error.NullValue);

        // Act
        var ensuredResult = result.Ensure(x => x > 0, Error.NullValue);

        // Assert
        ensuredResult.ShouldBe(result);
    }

    [Fact]
    public void Map_WithSuccess_ShouldMapToNewResult()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var mappedResult = result.Map(x => x.ToString(CultureInfo.InvariantCulture));

        // Assert
        mappedResult.IsSuccess.ShouldBeTrue();
        mappedResult.Value.ShouldBe("42");
    }

    [Fact]
    public void Map_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var result = Result.Failure<int>(Error.NullValue);

        // Act
        var mappedResult = result.Map(x => x.ToString(CultureInfo.InvariantCulture));

        // Assert
        mappedResult.IsSuccess.ShouldBeFalse();
        mappedResult.Errors.ShouldContain(Error.NullValue);
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
        boundResult.IsSuccess.ShouldBeTrue();
        boundResult.Value.ShouldBe(84);
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
        boundResult.IsSuccess.ShouldBeFalse();
        boundResult.Errors.ShouldContain(Error.NullValue);
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
        boundResult.IsSuccess.ShouldBeFalse();
        boundResult.IsFailure.ShouldBeTrue();
        boundResult.Errors.ShouldBeEquivalentTo(originalResult.Errors);
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
        actionInvoked.ShouldBeTrue();
        tappedResult.ShouldBe(result);
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
        actionInvoked.ShouldBeFalse();
        tappedResult.ShouldBe(result);
    }

    [Fact]
    public async Task Create_WithSuccess_ShouldReturnMappedResult()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success(42));
        static string func(int i) => (i * 2).ToString(CultureInfo.InvariantCulture);

        // Act
        var createdResult = await resultTask.Create(func);

        // Assert
        createdResult.IsSuccess.ShouldBeTrue();
        createdResult.Value.ShouldBe("84");
    }

    [Fact]
    public async Task Create_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Failure<int>(Error.NullValue));
        static string func(int i) => (i * 2).ToString(CultureInfo.InvariantCulture);

        // Act
        var createdResult = await resultTask.Create(func);

        // Assert
        createdResult.IsSuccess.ShouldBeFalse();
        createdResult.Errors.ShouldContain(Error.NullValue);
    }

    [Fact]
    public void TryCatch_WithSuccessAndNonThrowingFunc_ShouldReturnMappedResult()
    {
        // Arrange
        var result = Result.Success(42);
        static string func(int i) => (i * 2).ToString(CultureInfo.InvariantCulture);

        // Act
        var tryCatchResult = result.TryCatch(func, Error.NullValue);

        // Assert
        tryCatchResult.IsSuccess.ShouldBeTrue();
        tryCatchResult.Value.ShouldBe("84");
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
        tryCatchResult.IsSuccess.ShouldBeFalse();
        tryCatchResult.Errors.ShouldContain(Error.NullValue);
    }

    [Fact]
    public void TryCatch_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var result = Result.Failure<int>(Error.NullValue);
        static string func(int i) => (i * 2).ToString(CultureInfo.InvariantCulture);

        // Act
        var tryCatchResult = result.TryCatch(func, Error.None);

        // Assert
        tryCatchResult.IsSuccess.ShouldBeFalse();
        tryCatchResult.Errors.ShouldContain(Error.NullValue);
    }

    [Fact]
    public async Task TryCatchAsync_WithSuccessAndNonThrowingFunc_ShouldReturnMappedResult()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success(42));
        static string func(int i) => (i * 2).ToString(CultureInfo.InvariantCulture);

        // Act
        var tryCatchResult = await resultTask.TryCatch(func, Error.NullValue);

        // Assert
        tryCatchResult.IsSuccess.ShouldBeTrue();
        tryCatchResult.Value.ShouldBe("84");
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
        tryCatchResult.IsSuccess.ShouldBeFalse();
        tryCatchResult.Errors.ShouldContain(Error.NullValue);
    }

    [Fact]
    public async Task TryCatchAsync_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Failure<int>(Error.NullValue));
        static string func(int i) => (i * 2).ToString(CultureInfo.InvariantCulture);

        // Act
        var tryCatchResult = await resultTask.TryCatch(func, Error.None);

        // Assert
        tryCatchResult.IsSuccess.ShouldBeFalse();
        tryCatchResult.Errors.ShouldContain(Error.NullValue);
    }

    [Fact]
    public async Task BindAsync_WithSuccessAndPassingAsyncFunc_ShouldReturnResultFromFunc()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success(42));
        static Task<Result<string>> func(int i) => Task.FromResult(Result.Success(i.ToString(CultureInfo.InvariantCulture)));

        // Act
        var boundResult = await resultTask.Bind(func);

        // Assert
        boundResult.IsSuccess.ShouldBeTrue();
        boundResult.Value.ShouldBe("42");
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
        boundResult.IsSuccess.ShouldBeFalse();
        boundResult.Errors.ShouldContain(Error.NullValue);
    }

    [Fact]
    public async Task BindAsync_WithFailure_ShouldReturnOriginalFailureResult()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Failure<int>(Error.NullValue));
        static Task<Result<string>> func(int i) => Task.FromResult(Result.Success(i.ToString(CultureInfo.InvariantCulture)));

        // Act
        var boundResult = await resultTask.Bind(func);

        // Assert
        boundResult.IsSuccess.ShouldBeFalse();
        boundResult.Errors.ShouldContain(Error.NullValue);
    }

    [Fact]
    public void Match_WithSuccess_ShouldInvokeOnSuccess()
    {
        // Arrange
        var result = Result.Success(42);
        static string onSuccess(int x) => $"Success: {x}";
        static string onFailure(Error[] errors) => "Failure";

        // Act
        var matchResult = result.Match(onSuccess, onFailure);

        // Assert
        matchResult.ShouldBe("Success: 42");
    }

    [Fact]
    public void Match_WithFailure_ShouldInvokeOnFailure()
    {
        // Arrange
        var result = Result.Failure<int>(Error.NullValue);
        static string onSuccess(int x) => $"Success: {x}";
        static string onFailure(Error[] errors) => $"Failure: {errors[0].Code}";

        // Act
        var matchResult = result.Match(onSuccess, onFailure);

        // Assert
        matchResult.ShouldBe($"Failure: {Error.NullValue.Code}");
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
        matchResult.ShouldBe("Success: 42");
    }

    [Fact]
    public async Task MatchAsync_WithFailure_ShouldInvokeOnFailure()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Failure<int>(Error.NullValue));
        static string onSuccess(int x) => $"Success: {x}";
        static string onFailure(Error[] errors) => $"Failure: {errors[0].Code}";

        // Act
        var matchResult = await resultTask.Match(onSuccess, onFailure);

        // Assert
        matchResult.ShouldBe($"Failure: {Error.NullValue.Code}");
    }

}
