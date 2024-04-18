using System;

namespace Resrcify.SharedKernel.ResultFramework.Shared;

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
    public Error(string code, string message, ErrorType errorType)
    {
        Code = code;
        Message = message;
        Type = errorType;
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

    public static bool operator !=(Error? a, Error? b) => !(a == b);

    public virtual bool Equals(Error? other)
    {
        if (other is null)
            return false;

        return Code == other.Code && Message == other.Message;
    }

    public override bool Equals(object? obj) => obj is Error error && Equals(error);

    public override int GetHashCode() => HashCode.Combine(Code, Message);

    public override string ToString() => Code;
}
