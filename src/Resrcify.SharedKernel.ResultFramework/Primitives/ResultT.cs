﻿using System;
using System.Text.Json.Serialization;

namespace Resrcify.SharedKernel.ResultFramework.Primitives;

public class Result<TValue> : Result
{
    private readonly TValue? _value;

    protected internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) =>
        _value = value;

    [JsonConstructor]
    protected internal Result(TValue? value, bool isSuccess, Error[] errors)
        : base(isSuccess, errors) =>
        _value = value;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failure result can not be accessed.");

    public static implicit operator Result<TValue>(TValue? value)
        => Create(value);
    public static implicit operator Result<TValue>(Error error)
        => Failure<TValue>(error);
    public static implicit operator Result<TValue>(Error[] errors)
        => Failure<TValue>(errors);
}
