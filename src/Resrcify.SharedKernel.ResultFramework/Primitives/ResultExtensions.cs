using System;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.ResultFramework.Primitives;

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
        Func<TIn, Task<bool>> predicate,
        Error error)
    {
        if (result.IsFailure)
            return result;

        return await predicate(result.Value)
            ? result
            : Result.Failure<TIn>(error);
    }

    public static async Task<Result<TIn>> Ensure<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<bool>> predicate,
        Error error)
    {
        var result = await resultTask;

        if (result.IsFailure)
            return result;

        return await predicate(result.Value)
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
        var result = await resultTask;
        return result.IsSuccess
                ? Result.Success(mappingFunc(result.Value))
                : Result.Failure<TOut>(result.Errors);
    }

    public static async Task<Result<TOut>> Map<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<TOut>> mappingFunc)
    {
        var result = await resultTask;
        return result.IsSuccess
                ? Result.Success(await mappingFunc(result.Value))
                : Result.Failure<TOut>(result.Errors);
    }

    public static async Task<Result<TOut>> Map<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, Task<TOut>> mappingFunc)
        => result.IsSuccess
                ? Result.Success(await mappingFunc(result.Value))
                : Result.Failure<TOut>(result.Errors);

    public static Result Bind<TIn>(
        this Result<TIn> result,
        Func<TIn, Result> func)
        => result.IsFailure
            ? Result.Failure(result.Errors)
            : func(result.Value);

    public static async Task<Result> Bind<TIn>(
        this Result<TIn> result,
        Func<TIn, Task<Result>> func)
        => result.IsFailure
            ? Result.Failure(result.Errors)
            : await func(result.Value);

    public static async Task<Result> Bind<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<Result>> func)
    {
        var result = await resultTask;
        return result.IsFailure
            ? Result.Failure(result.Errors)
            : await func(result.Value);
    }

    public static async Task<Result> Bind<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Result> func)
    {
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
        Func<TIn, Task<Result<TOut>>> func)
        => result.IsFailure
            ? Result.Failure<TOut>(result.Errors)
            : await func(result.Value);

    public static async Task<Result<TOut>> Bind<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<Result<TOut>>> func)
    {
        var result = await resultTask;
        return result.IsFailure
            ? Result.Failure<TOut>(result.Errors)
            : await func(result.Value);
    }

    public static async Task<Result<TOut>> Bind<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Result<TOut>> func)
    {
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

    public static async Task<Result<TIn>> Tap<TIn>(
        this Result<TIn> result,
        Func<Task> func)
    {
        if (result.IsSuccess)
            await func();

        return result;
    }
    public static async Task<Result<TIn>> Tap<TIn>(
        this Result<TIn> result,
        Func<TIn, Task> func)
    {
        if (result.IsSuccess)
            await func(result.Value);

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

    public static async Task<Result<TIn>> Tap<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<Result>> func)
    {
        var result = await resultTask;
        if (result.IsFailure)
            return result;

        var value = await func(result.Value);
        return value.IsSuccess
            ? result
            : Result.Failure<TIn>(value.Errors);
    }
    public static async Task<Result<TIn>> Tap<TIn>(
        this Result<TIn> result,
        Func<TIn, Task<Result>> func)
    {
        if (result.IsFailure)
            return result;

        var value = await func(result.Value);
        return value.IsSuccess
            ? result
            : Result.Failure<TIn>(value.Errors);
    }

    public static async Task<Result<TOut>> Create<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> func)
    {
        var result = await resultTask;
        if (result.IsFailure)
            return Result.Failure<TOut>(result.Errors);

        var value = func(result.Value);
        return Result.Success(value);
    }

    public static async Task<Result<TOut>> Create<TIn, TOut>(
       this Task<Result<TIn>> resultTask,
       Func<TIn, Task<TOut>> func)
    {
        var result = await resultTask;
        if (result.IsFailure)
            return Result.Failure<TOut>(result.Errors);

        var value = await func(result.Value);
        return Result.Success(value);
    }
    public static async Task<Result<TOut>> Create<TIn, TOut>(
       this Result<TIn> result,
       Func<TIn, Task<TOut>> func)
    {
        if (result.IsFailure)
            return Result.Failure<TOut>(result.Errors);

        var value = await func(result.Value);
        return Result.Success(value);
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
        => result.IsSuccess
            ? onSuccess(result.Value)
            : onFailure(result.Errors);

    public static Result<TOut> Match<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> func,
        Error? error = null)
    {
        if (result.IsFailure && error is not null)
            return Result.Failure<TOut>(error);

        if (result.IsFailure && error is null)
            return Result.Failure<TOut>(result.Errors);

        var value = func(result.Value);
        return Result.Success(value);
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
