using System;

namespace Resrcify.SharedKernel.ResultFramework.Primitives;

public class Error : IEquatable<Error>
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);
    public static readonly Error NullValue = new("Error.NullValue", "The specified result value is null.", ErrorType.Failure);
    public static Error NotFound(string code, string message)
        => new(code, message, ErrorType.NotFound);
    public static Error Validation(string code, string message)
        => new(code, message, ErrorType.Validation);
    public static Error Conflict(string code, string message)
        => new(code, message, ErrorType.Conflict);
    public static Error Failure(string code, string message)
        => new(code, message, ErrorType.Failure);
    public Error(string code, string message, ErrorType type)
    {
        Code = code;
        Message = message;
        Type = type;
    }

    public string Code { get; init; }
    public string Message { get; init; }
    public ErrorType Type { get; init; }

    public static implicit operator string(Error error) => error.Code;
    public static implicit operator Result(Error error) => Result.Failure(error);

    public static bool operator ==(Error? a, Error? b)
    {
        if (a is null && b is null)
            return true;

        if (a is null || b is null)
            return false;

        return a.Equals(b);
    }

    public static bool operator !=(Error? a, Error? b)
        => !(a == b);

    public virtual bool Equals(Error? other)
        => other is not null &&
            Code == other.Code &&
            Message == other.Message &&
            Type == other.Type;

    public override bool Equals(object? obj)
        => obj is Error error && Equals(error);

    public override int GetHashCode()
        => HashCode.Combine(Code, Message, Type);

    public override string ToString()
        => Code;
}
