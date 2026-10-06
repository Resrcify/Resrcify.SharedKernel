using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.Results.Primitives;

public static class ResultExtensions
{
    public static Result<TIn> Ensure<TIn>(
        this Result<TIn> result,
        Func<TIn, bool> predicate,
        Error error)
    {
        if (result.IsFailure)
            return result;

        return predicate(result.Value)
            ? result
            : Result.Failure<TIn>(error);
    }

    public static async Task<Result<TIn>> Ensure<TIn>(
        this Result<TIn> result,
        Func<TIn, Task<bool>> asyncPredicate,
        Error error)
    {
        if (result.IsFailure)
            return result;

        return await asyncPredicate(result.Value)
            ? result
            : Result.Failure<TIn>(error);
    }

    public static Task<Result<TIn>> Ensure<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<bool>> asyncPredicate,
        Error error)
    {
        if (TaskUtils.TryGetResult(resultTask, out var completedResult))
        {
            if (completedResult.IsFailure)
                return Task.FromResult(completedResult);

            var predicateTask = asyncPredicate(completedResult.Value);
            if (TaskUtils.TryGetResult(predicateTask, out var predicateResult))
            {
                return Task.FromResult(predicateResult
                    ? completedResult
                    : Result.Failure<TIn>(error));
            }

            return EnsureFromPendingPredicate(completedResult, predicateTask, error);
        }

        return EnsureAwaited(resultTask, asyncPredicate, error);

        static async Task<Result<TIn>> EnsureFromPendingPredicate(
            Result<TIn> result,
            Task<bool> predicateTask,
            Error error)
            => await predicateTask
                ? result
                : Result.Failure<TIn>(error);

        static async Task<Result<TIn>> EnsureAwaited(
            Task<Result<TIn>> resultTask,
            Func<TIn, Task<bool>> asyncPredicate,
            Error error)
        {
            var result = await resultTask;

            if (result.IsFailure)
                return result;

            return await asyncPredicate(result.Value)
                ? result
                : Result.Failure<TIn>(error);
        }
    }

    public static async Task<Result<TIn>> Ensure<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, bool> predicate,
        Error error)
    {
        var result = await resultTask;

        if (result.IsFailure)
            return result;

        return predicate(result.Value)
            ? result
            : Result.Failure<TIn>(error);
    }

    public static Result<TOut> Map<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> mappingFunc)
        => result.IsSuccess
            ? Result.Success(mappingFunc(result.Value))
            : Result.Failure<TOut>(result.Errors);

    public static async Task<Result<TOut>> Map<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> mappingFunc)
    {
        if (TaskUtils.TryGetResult(resultTask, out var completedResult))
        {
            return completedResult.IsSuccess
                ? Result.Success(mappingFunc(completedResult.Value))
                : Result.Failure<TOut>(completedResult.Errors);
        }

        var result = await resultTask;
        return result.IsSuccess
                ? Result.Success(mappingFunc(result.Value))
                : Result.Failure<TOut>(result.Errors);
    }

    public static Task<Result<TOut>> Map<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<TOut>> asyncMappingFunc)
    {
        if (TaskUtils.TryGetResult(resultTask, out var completedResult))
        {
            if (completedResult.IsFailure)
                return Task.FromResult(Result.Failure<TOut>(completedResult.Errors));

            var mappingTask = asyncMappingFunc(completedResult.Value);
            if (TaskUtils.TryGetResult(mappingTask, out var mappedValue))
                return Task.FromResult(Result.Success(mappedValue));

            return MapFromPendingTask(mappingTask);
        }

        return MapAwaited(resultTask, asyncMappingFunc);

        static async Task<Result<TOut>> MapFromPendingTask(Task<TOut> mappingTask)
            => Result.Success(await mappingTask);

        static async Task<Result<TOut>> MapAwaited(
            Task<Result<TIn>> resultTask,
            Func<TIn, Task<TOut>> asyncMappingFunc)
        {
            var result = await resultTask;
            return result.IsSuccess
                    ? Result.Success(await asyncMappingFunc(result.Value))
                    : Result.Failure<TOut>(result.Errors);
        }
    }

    public static async Task<Result<TOut>> Map<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, Task<TOut>> asyncMappingFunc)
        => result.IsSuccess
                ? Result.Success(await asyncMappingFunc(result.Value))
                : Result.Failure<TOut>(result.Errors);

    /// <summary>Maps a success like <c>Map</c>; a failure becomes <paramref name="failureError"/> instead of its own errors.</summary>
    public static Result<TOut> Map<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> mappingFunc,
        Error failureError)
        => result.IsSuccess
            ? Result.Success(mappingFunc(result.Value))
            : Result.Failure<TOut>(failureError);

    public static Result Bind<TIn>(
        this Result<TIn> result,
        Func<TIn, Result> func)
        => result.IsFailure
            ? Result.Failure(result.Errors)
            : func(result.Value);

    public static async Task<Result> Bind<TIn>(
        this Result<TIn> result,
        Func<TIn, Task<Result>> asyncFunc)
        => result.IsFailure
            ? Result.Failure(result.Errors)
            : await asyncFunc(result.Value);

    public static Task<Result> Bind<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<Result>> asyncFunc)
    {
        if (TaskUtils.TryGetResult(resultTask, out var completedResult))
        {
            if (completedResult.IsFailure)
                return Task.FromResult(Result.Failure(completedResult.Errors));

            var bindTask = asyncFunc(completedResult.Value);
            if (TaskUtils.TryGetResult(bindTask, out var bindResult))
                return Task.FromResult(bindResult);

            return bindTask;
        }

        return BindAwaited(resultTask, asyncFunc);

        static async Task<Result> BindAwaited(
            Task<Result<TIn>> resultTask,
            Func<TIn, Task<Result>> asyncFunc)
        {
            var result = await resultTask;
            return result.IsFailure
                ? Result.Failure(result.Errors)
                : await asyncFunc(result.Value);
        }
    }

    public static async Task<Result> Bind<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Result> func)
    {
        if (TaskUtils.TryGetResult(resultTask, out var completedResult))
        {
            return completedResult.IsFailure
                ? Result.Failure(completedResult.Errors)
                : func(completedResult.Value);
        }

        var result = await resultTask;
        return result.IsFailure
            ? Result.Failure(result.Errors)
            : func(result.Value);
    }

    public static Result<TOut> Bind<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, Result<TOut>> func)
        => result.IsFailure
            ? Result.Failure<TOut>(result.Errors)
            : func(result.Value);

    public static async Task<Result<TOut>> Bind<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, Task<Result<TOut>>> asyncFunc)
        => result.IsFailure
            ? Result.Failure<TOut>(result.Errors)
            : await asyncFunc(result.Value);

    public static Task<Result<TOut>> Bind<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<Result<TOut>>> asyncFunc)
    {
        if (TaskUtils.TryGetResult(resultTask, out var completedResult))
        {
            if (completedResult.IsFailure)
                return Task.FromResult(Result.Failure<TOut>(completedResult.Errors));

            var bindTask = asyncFunc(completedResult.Value);
            if (TaskUtils.TryGetResult(bindTask, out var bindResult))
                return Task.FromResult(bindResult);

            return bindTask;
        }

        return BindAwaited(resultTask, asyncFunc);

        static async Task<Result<TOut>> BindAwaited(
            Task<Result<TIn>> resultTask,
            Func<TIn, Task<Result<TOut>>> asyncFunc)
        {
            var result = await resultTask;
            return result.IsFailure
                ? Result.Failure<TOut>(result.Errors)
                : await asyncFunc(result.Value);
        }
    }

    public static async Task<Result<TOut>> Bind<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Result<TOut>> func)
    {
        if (TaskUtils.TryGetResult(resultTask, out var completedResult))
        {
            return completedResult.IsFailure
                ? Result.Failure<TOut>(completedResult.Errors)
                : func(completedResult.Value);
        }

        var result = await resultTask;
        return result.IsFailure
            ? Result.Failure<TOut>(result.Errors)
            : func(result.Value);
    }

    public static Result<TIn> Tap<TIn>(
        this Result<TIn> result,
        Action<TIn> action)
    {
        if (result.IsSuccess)
            action(result.Value);

        return result;
    }

    // Runs a step that can fail: its failure becomes the result's (as the Task form of Tap does).
    public static Result<TIn> Tap<TIn>(
        this Result<TIn> result,
        Func<TIn, Result> func)
    {
        if (result.IsFailure)
            return result;

        var value = func(result.Value);
        return value.IsSuccess
            ? result
            : Result.Failure<TIn>(value.Errors);
    }

    public static async Task<Result<TIn>> Tap<TIn>(
        this Result<TIn> result,
        Func<Task> asyncFunc)
    {
        if (result.IsSuccess)
            await asyncFunc();

        return result;
    }
    public static async Task<Result<TIn>> Tap<TIn>(
        this Result<TIn> result,
        Func<TIn, Task> asyncFunc)
    {
        if (result.IsSuccess)
            await asyncFunc(result.Value);

        return result;
    }

    public static async Task<Result<TIn>> Tap<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task> asyncFunc)
    {
        var result = await resultTask;

        if (result.IsSuccess)
            await asyncFunc(result.Value);

        return result;
    }

    public static async Task<Result<TIn>> Tap<TIn>(
        this Task<Result<TIn>> resultTask,
        Action<TIn> action)
    {
        var result = await resultTask;
        if (result.IsSuccess)
            action(result.Value);

        return result;
    }
    public static async Task<Result<TIn>> Tap<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Result> func)
    {
        var result = await resultTask;
        if (result.IsFailure)
            return result;

        var value = func(result.Value);
        return value.IsSuccess
            ? result
            : Result.Failure<TIn>(value.Errors);
    }

    public static Task<Result<TIn>> Tap<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<Result>> asyncFunc)
    {
        if (TaskUtils.TryGetResult(resultTask, out var completedResult))
        {
            if (completedResult.IsFailure)
                return Task.FromResult(completedResult);

            var tapTask = asyncFunc(completedResult.Value);
            if (TaskUtils.TryGetResult(tapTask, out var tapResult))
            {
                return Task.FromResult(tapResult.IsSuccess
                    ? completedResult
                    : Result.Failure<TIn>(tapResult.Errors));
            }

            return TapFromPendingTask(completedResult, tapTask);
        }

        return TapAwaited(resultTask, asyncFunc);

        static async Task<Result<TIn>> TapFromPendingTask(Result<TIn> result, Task<Result> tapTask)
        {
            var value = await tapTask;
            return value.IsSuccess
                ? result
                : Result.Failure<TIn>(value.Errors);
        }

        static async Task<Result<TIn>> TapAwaited(
            Task<Result<TIn>> resultTask,
            Func<TIn, Task<Result>> asyncFunc)
        {
            var result = await resultTask;
            if (result.IsFailure)
                return result;

            var value = await asyncFunc(result.Value);
            return value.IsSuccess
                ? result
                : Result.Failure<TIn>(value.Errors);
        }
    }
    public static async Task<Result<TIn>> Tap<TIn>(
        this Result<TIn> result,
        Func<TIn, Task<Result>> asyncFunc)
    {
        if (result.IsFailure)
            return result;

        var value = await asyncFunc(result.Value);
        return value.IsSuccess
            ? result
            : Result.Failure<TIn>(value.Errors);
    }

    // A cancellation isn't a failure of func: it is rethrown, so the caller stops as it asked to.
    public static Result<TOut> TryCatch<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> func,
        Error error)
        => result.TryCatch(func, _ => error);

    public static Result<TOut> TryCatch<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> func,
        Func<Exception, Error> onException)
    {
        try
        {
            return result.IsSuccess
                ? Result.Success(func(result.Value))
                : Result.Failure<TOut>(result.Errors);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure<TOut>(onException(exception));
        }
    }

    public static Task<Result<TOut>> TryCatch<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> func,
        Error error)
        => resultTask.TryCatch(func, _ => error);

    public static async Task<Result<TOut>> TryCatch<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> func,
        Func<Exception, Error> onException)
    {
        try
        {
            var result = await resultTask;
            return result.IsSuccess
                ? Result.Success(func(result.Value))
                : Result.Failure<TOut>(result.Errors);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure<TOut>(onException(exception));
        }
    }
    public static TOut Match<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> onSuccess,
        Func<IReadOnlyList<Error>, TOut> onFailure)
        => result.IsSuccess
            ? onSuccess(result.Value)
            : onFailure(result.Errors);
    public static async Task<TOut> Match<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> onSuccess,
        Func<IReadOnlyList<Error>, TOut> onFailure)
    {
        if (TaskUtils.TryGetResult(resultTask, out var completedResult))
        {
            return completedResult.IsSuccess
                ? onSuccess(completedResult.Value)
                : onFailure(completedResult.Errors);
        }

        var result = await resultTask;
        return result.IsSuccess
            ? onSuccess(result.Value)
            : onFailure(result.Errors);
    }
    public static async Task<TOut> Match<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, Task<TOut>> onSuccess,
        Func<IReadOnlyList<Error>, Task<TOut>> onFailure)
    {
        return result.IsSuccess
            ? await onSuccess(result.Value)
            : await onFailure(result.Errors);
    }
    public static Task<TOut> Match<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<TOut>> onSuccess,
        Func<IReadOnlyList<Error>, Task<TOut>> onFailure)
    {
        if (TaskUtils.TryGetResult(resultTask, out var completedResult))
        {
            var matchTask = completedResult.IsSuccess
                ? onSuccess(completedResult.Value)
                : onFailure(completedResult.Errors);

            if (TaskUtils.TryGetResult(matchTask, out var matchResult))
                return Task.FromResult(matchResult);

            return matchTask;
        }

        return MatchAwaited(resultTask, onSuccess, onFailure);

        static async Task<TOut> MatchAwaited(
            Task<Result<TIn>> resultTask,
            Func<TIn, Task<TOut>> onSuccess,
            Func<IReadOnlyList<Error>, Task<TOut>> onFailure)
        {
            var result = await resultTask;
            return result.IsSuccess
                ? await onSuccess(result.Value)
                : await onFailure(result.Errors);
        }
    }
    public static async Task<TOut> Match<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, Task<TOut>> onSuccess,
        Func<IReadOnlyList<Error>, TOut> onFailure)
        => result.IsSuccess
            ? await onSuccess(result.Value)
            : onFailure(result.Errors);
    public static async Task<TOut> Match<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<TOut>> onSuccess,
        Func<IReadOnlyList<Error>, TOut> onFailure)
    {
        var result = await resultTask;
        return result.IsSuccess
            ? await onSuccess(result.Value)
            : onFailure(result.Errors);
    }
    public static async Task<TOut> Match<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> onSuccess,
        Func<IReadOnlyList<Error>, Task<TOut>> onFailure)
        => result.IsSuccess
            ? onSuccess(result.Value)
            : await onFailure(result.Errors);
    public static async Task<TOut> Match<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> onSuccess,
        Func<IReadOnlyList<Error>, Task<TOut>> onFailure)
    {
        var result = await resultTask;
        return result.IsSuccess
            ? onSuccess(result.Value)
            : await onFailure(result.Errors);
    }

    /// <summary>
    /// <paramref name="value"/> as a success, or <paramref name="errorIfNull"/> when it is <see langword="null"/>: how a
    /// handler turns a repository fetch into its own <c>NotFound</c> error.
    /// </summary>
    /// <example><c>var shard = (await shards.GetByIdAsync(id, ct)).ToResult(DomainErrors.Shard.NotFound(id));</c></example>
    public static Result<T> ToResult<T>(
        this T? value,
        Error errorIfNull)
        where T : class
        => value is null
            ? Result.Failure<T>(errorIfNull)
            : Result.Success(value);

    /// <summary>
    /// <paramref name="value"/> as a success, or the error <paramref name="errorIfNull"/> makes when it is
    /// <see langword="null"/>. The error is only made when it is needed, so a formatted message costs nothing when the
    /// value is there.
    /// </summary>
    /// <example><c>var shard = found.ToResult(() =&gt; DomainErrors.Shard.NotFound(id));</c></example>
    public static Result<T> ToResult<T>(
        this T? value,
        Func<Error> errorIfNull)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(errorIfNull);
        return value is null
            ? Result.Failure<T>(errorIfNull())
            : Result.Success(value);
    }

    /// <inheritdoc cref="ToResult{T}(T, Error)"/>
    /// <example><c>var shard = await shards.GetByIdAsync(id, ct).ToResultAsync(DomainErrors.Shard.NotFound(id));</c></example>
    public static async Task<Result<T>> ToResultAsync<T>(
        this Task<T?> valueTask,
        Error errorIfNull)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(valueTask);
        return (await valueTask).ToResult(errorIfNull);
    }

    /// <inheritdoc cref="ToResult{T}(T, Func{Error})"/>
    /// <example><c>var shard = await shards.GetByIdAsync(id, ct).ToResultAsync(() =&gt; DomainErrors.Shard.NotFound(id));</c></example>
    public static async Task<Result<T>> ToResultAsync<T>(
        this Task<T?> valueTask,
        Func<Error> errorIfNull)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(valueTask);
        return (await valueTask).ToResult(errorIfNull);
    }
}
