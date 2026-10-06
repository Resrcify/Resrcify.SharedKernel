using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;


namespace Resrcify.SharedKernel.Results.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
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
        static string onFailure(IReadOnlyList<Error> errors) => "Failure";

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
        static string onFailure(IReadOnlyList<Error> errors) => $"Failure: {errors[0].Code}";

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
        static string onFailure(IReadOnlyList<Error> errors) => "Failure";

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
        static string onFailure(IReadOnlyList<Error> errors) => $"Failure: {errors[0].Code}";

        // Act
        var matchResult = await resultTask.Match(onSuccess, onFailure);

        // Assert
        matchResult.ShouldBe($"Failure: {Error.NullValue.Code}");
    }

    [Fact]
    public void Tap_WithResultFunc_ShouldReturnTheOriginalResult_WhenTheStepSucceeds()
    {
        // Arrange
        var result = Result.Success(42);
        var seen = 0;

        // Act
        var tapped = result.Tap(value =>
        {
            seen = value;
            return Result.Success();
        });

        // Assert
        tapped.ShouldBeSameAs(result);
        seen.ShouldBe(42);
    }

    [Fact]
    public void Tap_WithResultFunc_ShouldReturnTheStepsErrors_WhenTheStepFails()
    {
        // Arrange
        var result = Result.Success(42);

        // Act
        var tapped = result.Tap(_ => Result.Failure(Error.NullValue));

        // Assert
        tapped.IsFailure.ShouldBeTrue();
        tapped.Errors.ShouldBe([Error.NullValue]);
    }

    [Fact]
    public void Tap_WithResultFunc_ShouldNotRunTheStep_WhenTheResultIsAFailure()
    {
        // Arrange
        var result = Result.Failure<int>(Error.EmptyInput);
        var ran = false;

        // Act
        var tapped = result.Tap(_ =>
        {
            ran = true;
            return Result.Success();
        });

        // Assert
        ran.ShouldBeFalse();
        tapped.Errors.ShouldBe([Error.EmptyInput]);
    }

    [Fact]
    public void TryCatch_ShouldRethrow_WhenTheFuncIsCancelled()
    {
        // Arrange
        var result = Result.Success(42);
        static string func(int i) => throw new OperationCanceledException();

        // Act & Assert
        Should.Throw<OperationCanceledException>(() => result.TryCatch(func, Error.NullValue));
    }

    [Fact]
    public async Task TryCatchAsync_ShouldRethrow_WhenTheResultTaskIsCancelled()
    {
        // Arrange
        var resultTask = Task.FromCanceled<Result<int>>(new System.Threading.CancellationToken(canceled: true));

        // Act & Assert
        await Should.ThrowAsync<TaskCanceledException>(() => resultTask.TryCatch(i => i.ToString(CultureInfo.InvariantCulture), Error.NullValue));
    }

    [Fact]
    public void TryCatch_ShouldMapTheException_WhenGivenAnExceptionMapper()
    {
        // Arrange
        var result = Result.Success(42);
        static string func(int i) => throw new FormatException("bad format");

        // Act
        var tryCatchResult = result.TryCatch(func, exception => Error.Validation("Parse", exception.Message));

        // Assert
        tryCatchResult.Errors.ShouldHaveSingleItem().ShouldBe(Error.Validation("Parse", "bad format"));
    }

    [Fact]
    public async Task TryCatchAsync_ShouldMapTheException_WhenGivenAnExceptionMapper()
    {
        // Arrange
        var resultTask = Task.FromResult(Result.Success(42));
        static string func(int i) => throw new FormatException("bad format");

        // Act
        var tryCatchResult = await resultTask.TryCatch(func, exception => Error.Validation("Parse", exception.Message));

        // Assert
        tryCatchResult.Errors.ShouldHaveSingleItem().ShouldBe(Error.Validation("Parse", "bad format"));
    }

    [Fact]
    public void ToResult_ShouldBeASuccess_WhenThereIsAValue()
    {
        var value = new Box("found");

        var result = value.ToResult(Error.NotFound("Box.NotFound", "No box."));

        result.Value.ShouldBeSameAs(value);
    }

    [Fact]
    public void ToResult_ShouldBeTheGivenError_WhenTheValueIsNull()
    {
        Box? value = null;
        var notFound = Error.NotFound("Box.NotFound", "No box.");

        var result = value.ToResult(notFound);

        result.Errors.ShouldBe([notFound]);
    }

    [Fact]
    public async Task ToResultAsync_ShouldBeTheGivenError_WhenTheFetchFindsNothing()
    {
        var notFound = Error.NotFound("Box.NotFound", "No box.");

        var result = await Task.FromResult<Box?>(null).ToResultAsync(notFound);

        result.Errors.ShouldBe([notFound]);
    }

    [Fact]
    public async Task ToResultAsync_ShouldBeASuccess_WhenTheFetchFindsAValue()
    {
        var value = new Box("found");

        var result = await Task.FromResult<Box?>(value).ToResultAsync(Error.NullValue);

        result.Value.ShouldBeSameAs(value);
    }

    [Fact]
    public void ToResult_ShouldNotMakeTheError_WhenThereIsAValue()
    {
        var made = 0;

        var result = new Box("found").ToResult(() =>
        {
            made++;
            return Error.NotFound("Box.NotFound", "No box.");
        });

        result.IsSuccess.ShouldBeTrue();
        made.ShouldBe(0);
    }

    [Fact]
    public void ToResult_ShouldMakeTheError_WhenTheValueIsNull()
    {
        Box? value = null;

        var result = value.ToResult(() => Error.NotFound("Box.NotFound", "No box."));

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("Box.NotFound");
    }

    [Fact]
    public async Task ToResultAsync_ShouldMakeTheError_OnlyWhenTheFetchFindsNothing()
    {
        var made = 0;
        Error NotFound()
        {
            made++;
            return Error.NotFound("Box.NotFound", "No box.");
        }

        var found = await Task.FromResult<Box?>(new Box("found")).ToResultAsync(NotFound);
        var missing = await Task.FromResult<Box?>(null).ToResultAsync(NotFound);

        found.IsSuccess.ShouldBeTrue();
        missing.Errors.ShouldHaveSingleItem().Code.ShouldBe("Box.NotFound");
        made.ShouldBe(1);
    }

    [SuppressMessage(
        "Performance",
        "CA1515:Consider making public types internal",
        Justification = "Used by public test methods.")]
    public sealed record Box(string Name);
}
