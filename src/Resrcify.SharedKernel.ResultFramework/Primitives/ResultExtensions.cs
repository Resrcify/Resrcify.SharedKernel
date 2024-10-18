using System;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.ResultFramework.Primitives;

public static class ResultExtensions
{
    public static Result<T> Ensure<T>(
        this Result<T> result,
        Func<T, bool> predicate,
        Error error)
    {
        if (result.IsFailure)
            return result;

        return predicate(result.Value)
            ? result
            : Result.Failure<T>(error);
    }

    public static Result<TOut> Map<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> mappingFunc)
    {
        return result.IsSuccess
            ? Result.Success(mappingFunc(result.Value))
            : Result.Failure<TOut>(result.Errors);
    }

    public static async Task<Result> Bind<TIn>(
        this Result<TIn> result,
        Func<TIn, Task<Result>> func)
    {
        if (result.IsFailure)
            return Result.Failure(result.Errors);

        return await func(result.Value);
    }

    public static async Task<Result<TOut>> Bind<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, Task<Result<TOut>>> func)
    {
        if (result.IsFailure)
            return Result.Failure<TOut>(result.Errors);

        return await func(result.Value);
    }
    public static async Task<Result> Bind<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<Result>> func)
    {
        var result = await resultTask;
        if (result.IsFailure)
            return Result.Failure(result.Errors);

        return await func(result.Value);
    }

    public static async Task<Result<TOut>> Bind<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<Result<TOut>>> func)
    {
        var result = await resultTask;
        if (result.IsFailure)
            return Result.Failure<TOut>(result.Errors);

        return await func(result.Value);
    }

    public static Result<TIn> Tap<TIn>(
        this Result<TIn> result,
        Action<TIn> action)
    {
        if (result.IsSuccess)
            action(result.Value);

        return result;
    }

    public static async Task<Result<TIn>> Tap<TIn>(
        this Result<TIn> result,
        Func<Task> func)
    {
        if (result.IsSuccess)
            await func();

        return result;
    }

    public static async Task<Result<TIn>> Tap<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task> func)
    {
        var result = await resultTask;

        if (result.IsSuccess)
            await func(result.Value);

        return result;
    }

    public static async Task<Result<TOut>> Create<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> func)
    {
        var result = await resultTask;
        if (result.IsFailure)
            return Result.Failure<TOut>(result.Errors);

        var newValue = func(result.Value);
        return Result.Success(newValue);
    }

    public static Result<TOut> TryCatch<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> func,
        Error error)
    {
        try
        {
            return result.IsSuccess
                ? Result.Success(func(result.Value))
                : Result.Failure<TOut>(result.Errors);
        }
        catch
        {
            return Result.Failure<TOut>(error);
        }
    }
    public static async Task<Result<TOut>> TryCatch<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> func,
        Error error)
    {
        try
        {
            var result = await resultTask;
            return result.IsSuccess
                ? Result.Success(func(result.Value))
                : Result.Failure<TOut>(result.Errors);
        }
        catch
        {
            return Result.Failure<TOut>(error);
        }
    }

    public static TOut Match<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> onSuccess,
        Func<Error[], TOut> onFailure)
    {
        return result.IsSuccess
            ? onSuccess(result.Value)
            : onFailure(result.Errors);
    }

    public static async Task<TOut> Match<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> onSuccess,
        Func<Error[], TOut> onFailure)
    {
        var result = await resultTask;
        return result.IsSuccess
            ? onSuccess(result.Value)
            : onFailure(result.Errors);
    }
}
