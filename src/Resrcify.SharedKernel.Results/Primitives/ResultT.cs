using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Resrcify.SharedKernel.Results.Primitives;

public class Result<TValue>
    : Result
{
    protected internal Result(
        TValue? value,
        bool isSuccess,
        Error error)
        : base(isSuccess, error) =>
        Value = value;

    [JsonConstructor]
    protected internal Result(
        TValue? value,
        bool isSuccess,
        Error[] errors)
        : base(isSuccess, errors) =>
        Value = value;

    [AllowNull]
    public TValue Value => IsSuccess
        ? field!
        : throw new InvalidOperationException(
            "The value of a failure result can not be accessed.");

    public static implicit operator Result<TValue>(
        TValue? value)
        => Create(value);
    public static implicit operator Result<TValue>(
        Error error)
        => Failure<TValue>(error);
    public static implicit operator Result<TValue>(
        Error[] errors)
        => Failure<TValue>(errors);
}
