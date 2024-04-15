using System;
using System.Collections.Generic;
using System.Linq;

namespace Resrcify.SharedKernel.Shared;
public class Result
{
    protected internal Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException();
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException();
        }

        IsSuccess = isSuccess;
        Errors = [error];
    }

    protected internal Result(bool isSuccess, Error[] errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error[] Errors { get; }

    public static Result Success() => new(true, Error.None);

    public static Result<TValue> Success<TValue>(TValue value) =>
        new(value, true, Error.None);

    public static Result Failure(Error error) =>
        new(false, error);

    public static Result Failure(Error[] errors) =>
        new(false, errors);

    public static Result<TValue> Failure<TValue>(Error error) =>
        new(default, false, error);

    public static Result<TValue> Failure<TValue>(Error[] errors) =>
        new(default, false, errors);

    public static Result<TValue> Create<TValue>(TValue? value) =>
        value is not null ? Success(value) : Failure<TValue>(Error.NullValue);

    public static Result<T> Ensure<T>(T value, Func<T, bool> predicate, Error error)
    {
        return predicate(value) ?
            Success(value) :
            Failure<T>(error);
    }

    public static Result<T> Ensure<T>(
        T value,
        params (Func<T, bool> predicate, Error error)[] functions)
    {
        var results = new List<Result<T>>();
        foreach ((Func<T, bool> predicate, Error error) in functions)
            results.Add(Ensure(value, predicate, error));

        return Combine(results.ToArray());
    }

    public static Result<T> Combine<T>(params Result<T>[] results)
    {
        if (results.Any(r => r.IsFailure))
        {
            return Failure<T>(
                results
                    .SelectMany(r => r.Errors)
                    .Where(e => e != Error.None)
                    .Distinct()
                    .ToArray());
        }

        return Success(results[0].Value);
    }

    public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> result1, Result<T2> result2)
    {
        var failures = new List<Error>();

        if (result1.IsFailure)
            failures.AddRange(result1.Errors);

        if (result2.IsFailure)
            failures.AddRange(result2.Errors);

        if (failures.Count > 0)
            return Failure<(T1, T2)>(failures.ToArray());

        return Success((result1.Value, result2.Value));
    }

    public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(Result<T1> result1, Result<T2> result2, Result<T3> result3)
    {
        var failures = new List<Error>();
        if (result1.IsFailure)
            failures.AddRange(result1.Errors);

        if (result2.IsFailure)
            failures.AddRange(result2.Errors);

        if (result3.IsFailure)
            failures.AddRange(result3.Errors);

        if (failures.Count > 0)
            return Failure<(T1, T2, T3)>(failures.ToArray());

        return Success((result1.Value, result2.Value, result3.Value));
    }

    public static Result<(T1, T2, T3, T4)> Combine<T1, T2, T3, T4>(Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4)
    {
        var failures = new List<Error>();

        if (result1.IsFailure)
            failures.AddRange(result1.Errors);

        if (result2.IsFailure)
            failures.AddRange(result2.Errors);

        if (result3.IsFailure)
            failures.AddRange(result3.Errors);

        if (result4.IsFailure)
            failures.AddRange(result4.Errors);

        if (failures.Count > 0)
            return Failure<(T1, T2, T3, T4)>(failures.ToArray());

        return Success((result1.Value, result2.Value, result3.Value, result4.Value));
    }

    public static Result<(T1, T2, T3, T4, T5)> Combine<T1, T2, T3, T4, T5>(Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4, Result<T5> result5)
    {
        var failures = new List<Error>();

        if (result1.IsFailure)
            failures.AddRange(result1.Errors);

        if (result2.IsFailure)
            failures.AddRange(result2.Errors);

        if (result3.IsFailure)
            failures.AddRange(result3.Errors);

        if (result4.IsFailure)
            failures.AddRange(result4.Errors);

        if (result5.IsFailure)
            failures.AddRange(result5.Errors);

        if (failures.Count > 0)
            return Failure<(T1, T2, T3, T4, T5)>(failures.ToArray());

        return Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value));
    }
    public static Result<(T1, T2, T3, T4, T5, T6)> Combine<T1, T2, T3, T4, T5, T6>(Result<T1> result1, Result<T2> result2, Result<T3> result3, Result<T4> result4, Result<T5> result5, Result<T6> result6)
    {
        var failures = new List<Error>();

        if (result1.IsFailure)
            failures.AddRange(result1.Errors);

        if (result2.IsFailure)
            failures.AddRange(result2.Errors);

        if (result3.IsFailure)
            failures.AddRange(result3.Errors);

        if (result4.IsFailure)
            failures.AddRange(result4.Errors);

        if (result5.IsFailure)
            failures.AddRange(result5.Errors);

        if (result6.IsFailure)
            failures.AddRange(result6.Errors);

        if (failures.Count > 0)
            return Failure<(T1, T2, T3, T4, T5, T6)>(failures.ToArray());

        return Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value, result6.Value));
    }

}