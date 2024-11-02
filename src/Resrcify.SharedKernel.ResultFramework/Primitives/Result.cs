using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Resrcify.SharedKernel.ResultFramework.Primitives;
public class Result
{
    protected internal Result(
        bool isSuccess,
        Error error)
    {
        if (isSuccess && error != Error.None)
            throw new InvalidOperationException();

        if (!isSuccess && error == Error.None)
            throw new InvalidOperationException();

        IsSuccess = isSuccess;

        Errors = [];

        if (error != Error.None)
            Errors = [error];
    }

    [JsonConstructor]
    protected internal Result(
        bool isSuccess,
        Error[] errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error[] Errors { get; }

    public static Result Success() => new(true, Error.None);

    public static Result<TValue> Success<TValue>(TValue value)
        => new(
            value,
            true,
            Error.None);

    public static Result Failure(Error error)
        => new(
            false,
            error);

    public static Result Failure(Error[] errors)
        => new(
            false,
            errors);

    public static Result<TValue> Failure<TValue>(Error error)
        => new(
            default,
            false,
            error);

    public static Result<TValue> Failure<TValue>(Error[] errors)
        => new(
            default,
            false,
            errors);

    public static Result<TValue> Create<TValue>(TValue? value)
        => value is not null
            ? Success(value)
            : Failure<TValue>(Error.NullValue);

    public static Result<T> Ensure<T>(
        T value,
        Func<T, bool> predicate,
        Error error)
        => predicate(value) ?
            Success(value) :
            Failure<T>(error);

    public static Result<T> Ensure<T>(
        T value,
        params (Func<T, bool> predicate, Error error)[] functions)
        => Combine(
            functions
                .Select(result => Ensure(value, result.predicate, result.error))
                .ToArray());

    public static Result<T> Combine<T>(params Result<T>[] results)
    {
        var failures = CollectFailures(results);

        return failures.Length > 0
            ? Failure<T>(failures)
            : Success(results[0].Value);
    }

    public static Result<(T1, T2)> Combine<T1, T2>(
        Result<T1> result1,
        Result<T2> result2)
    {
        var failures = CollectFailures(
            result1,
            result2);

        return failures.Length > 0
            ? Failure<(T1, T2)>(failures)
            : Success((result1.Value, result2.Value));
    }

    public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3);

        return failures.Length > 0
            ? Failure<(T1, T2, T3)>(failures)
            : Success((result1.Value, result2.Value, result3.Value));
    }

    public static Result<(T1, T2, T3, T4)> Combine<T1, T2, T3, T4>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3,
        Result<T4> result4)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3,
            result4);

        return failures.Length > 0
            ? Failure<(T1, T2, T3, T4)>(failures)
            : Success((result1.Value, result2.Value, result3.Value, result4.Value));
    }

    public static Result<(T1, T2, T3, T4, T5)> Combine<T1, T2, T3, T4, T5>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3,
        Result<T4> result4,
        Result<T5> result5)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3,
            result4,
            result5);

        return failures.Length > 0
            ? Failure<(T1, T2, T3, T4, T5)>(failures)
            : Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value));
    }
    public static Result<(T1, T2, T3, T4, T5, T6)> Combine<T1, T2, T3, T4, T5, T6>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3,
        Result<T4> result4,
        Result<T5> result5,
        Result<T6> result6)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3,
            result4,
            result5,
            result6);

        return failures.Length > 0
            ? Failure<(T1, T2, T3, T4, T5, T6)>(failures)
            : Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value, result6.Value));
    }

    public static Result<(T1, T2, T3, T4, T5, T6, T7)> Combine<T1, T2, T3, T4, T5, T6, T7>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3,
        Result<T4> result4,
        Result<T5> result5,
        Result<T6> result6,
        Result<T7> result7)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3,
            result4,
            result5,
            result6,
            result7);

        return failures.Length > 0
            ? Failure<(T1, T2, T3, T4, T5, T6, T7)>(failures)
            : Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value, result6.Value, result7.Value));
    }
    public static Result<(T1, T2, T3, T4, T5, T6, T7, T8)> Combine<T1, T2, T3, T4, T5, T6, T7, T8>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3,
        Result<T4> result4,
        Result<T5> result5,
        Result<T6> result6,
        Result<T7> result7,
        Result<T8> result8)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3,
            result4,
            result5,
            result6,
            result7,
            result8);

        return failures.Length > 0
            ? Failure<(T1, T2, T3, T4, T5, T6, T7, T8)>(failures)
            : Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value, result6.Value, result7.Value, result8.Value));
    }
    public static Result<(T1, T2, T3, T4, T5, T6, T7, T8, T9)> Combine<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3,
        Result<T4> result4,
        Result<T5> result5,
        Result<T6> result6,
        Result<T7> result7,
        Result<T8> result8,
        Result<T9> result9)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3,
            result4,
            result5,
            result6,
            result7,
            result8,
            result9);

        return failures.Length > 0
            ? Failure<(T1, T2, T3, T4, T5, T6, T7, T8, T9)>(failures)
            : Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value, result6.Value, result7.Value, result8.Value, result9.Value));
    }
    public static Result<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10)> Combine<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3,
        Result<T4> result4,
        Result<T5> result5,
        Result<T6> result6,
        Result<T7> result7,
        Result<T8> result8,
        Result<T9> result9,
        Result<T10> result10)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3,
            result4,
            result5,
            result6,
            result7,
            result8,
            result9,
            result10);

        return failures.Length > 0
            ? Failure<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10)>(failures)
            : Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value, result6.Value, result7.Value, result8.Value, result9.Value, result10.Value));
    }

    public static Result<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11)> Combine<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3,
        Result<T4> result4,
        Result<T5> result5,
        Result<T6> result6,
        Result<T7> result7,
        Result<T8> result8,
        Result<T9> result9,
        Result<T10> result10,
        Result<T11> result11)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3,
            result4,
            result5,
            result6,
            result7,
            result8,
            result9,
            result10,
            result11);

        return failures.Length > 0
            ? Failure<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11)>(failures)
            : Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value, result6.Value, result7.Value, result8.Value, result9.Value, result10.Value, result11.Value));
    }
    public static Result<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12)> Combine<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3,
        Result<T4> result4,
        Result<T5> result5,
        Result<T6> result6,
        Result<T7> result7,
        Result<T8> result8,
        Result<T9> result9,
        Result<T10> result10,
        Result<T11> result11,
        Result<T12> result12)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3,
            result4,
            result5,
            result6,
            result7,
            result8,
            result9,
            result10,
            result11,
            result12);

        return failures.Length > 0
            ? Failure<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12)>(failures)
            : Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value, result6.Value, result7.Value, result8.Value, result9.Value, result10.Value, result11.Value, result12.Value));
    }

    public static Result<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13)> Combine<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3,
        Result<T4> result4,
        Result<T5> result5,
        Result<T6> result6,
        Result<T7> result7,
        Result<T8> result8,
        Result<T9> result9,
        Result<T10> result10,
        Result<T11> result11,
        Result<T12> result12,
        Result<T13> result13)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3,
            result4,
            result5,
            result6,
            result7,
            result8,
            result9,
            result10,
            result11,
            result12,
            result13);

        return failures.Length > 0
            ? Failure<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13)>(failures)
            : Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value, result6.Value, result7.Value, result8.Value, result9.Value, result10.Value, result11.Value, result12.Value, result13.Value));
    }
    public static Result<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14)> Combine<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3,
        Result<T4> result4,
        Result<T5> result5,
        Result<T6> result6,
        Result<T7> result7,
        Result<T8> result8,
        Result<T9> result9,
        Result<T10> result10,
        Result<T11> result11,
        Result<T12> result12,
        Result<T13> result13,
        Result<T14> result14)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3,
            result4,
            result5,
            result6,
            result7,
            result8,
            result9,
            result10,
            result11,
            result12,
            result13,
            result14);

        return failures.Length > 0
            ? Failure<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14)>(failures)
            : Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value, result6.Value, result7.Value, result8.Value, result9.Value, result10.Value, result11.Value, result12.Value, result13.Value, result14.Value));
    }
    public static Result<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15)> Combine<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(
        Result<T1> result1,
        Result<T2> result2,
        Result<T3> result3,
        Result<T4> result4,
        Result<T5> result5,
        Result<T6> result6,
        Result<T7> result7,
        Result<T8> result8,
        Result<T9> result9,
        Result<T10> result10,
        Result<T11> result11,
        Result<T12> result12,
        Result<T13> result13,
        Result<T14> result14,
        Result<T15> result15)
    {
        var failures = CollectFailures(
            result1,
            result2,
            result3,
            result4,
            result5,
            result6,
            result7,
            result8,
            result9,
            result10,
            result11,
            result12,
            result13,
            result14,
            result15);

        return failures.Length > 0
            ? Failure<(T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15)>(failures)
            : Success((result1.Value, result2.Value, result3.Value, result4.Value, result5.Value, result6.Value, result7.Value, result8.Value, result9.Value, result10.Value, result11.Value, result12.Value, result13.Value, result14.Value, result15.Value));
    }
    private static Error[] CollectFailures(params Result[] results)
        => results
            .Where(result => result.IsFailure)
            .SelectMany(failureResult => failureResult.Errors)
            .Where(e => e != Error.None)
            .Distinct()
            .ToArray();
}