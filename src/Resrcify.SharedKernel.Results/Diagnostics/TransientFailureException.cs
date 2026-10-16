using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Results.Diagnostics;

/// <summary>
/// Work failed for a reason another try may get past (a downstream service unreachable, a timeout, a rate limit), and
/// the code that saw it can't answer with a failed <see cref="Result"/>: a domain event handler in the outbox, a job. It
/// carries the failed call's <see cref="Errors"/>. Every SharedKernel package counts it as transient
/// (<see cref="TransientFailures.IsTransient(Exception)"/>, anywhere among an exception's causes): the outbox gives it
/// its transient tries, the mediator's behaviors and the jobs log it at <c>Warning</c>, the message bus shows its errors
/// as the reason a message failed, and the Web package answers it with problem details of its errors.
/// </summary>
/// <remarks>
/// Thrown from a failed result by <see cref="TransientFailures.ThrowIfTransient(Result)"/> (and its overloads), which
/// throws only when one of the result's errors is transient (<see cref="ErrorTypeExtensions.IsTransient(Error)"/>).
/// </remarks>
public sealed class TransientFailureException
    : Exception
{
    private const string DefaultMessage = "Work failed for a reason another try may get past.";

    public TransientFailureException()
        : this(DefaultMessage)
    {
    }

    public TransientFailureException(string message)
        : this(message, innerException: null)
    {
    }

    public TransientFailureException(
        string message,
        Exception? innerException)
        : base(message, innerException)
        => Errors = [];

    /// <summary>A transient failure with <paramref name="errors"/>, described in its message.</summary>
    /// <param name="errors">The failed call's errors.</param>
    public TransientFailureException(IEnumerable<Error> errors)
        : this(ToArray(errors), message: null, innerException: null)
    {
    }

    /// <summary>A transient failure with <paramref name="errors"/>, and a message of its own.</summary>
    /// <param name="message">What failed, e.g. the step and the call.</param>
    /// <param name="errors">The failed call's errors.</param>
    /// <param name="innerException">The exception behind it, if any.</param>
    public TransientFailureException(
        string message,
        IEnumerable<Error> errors,
        Exception? innerException = null)
        : this(ToArray(errors), message, innerException)
    {
    }

    private TransientFailureException(
        ImmutableArray<Error> errors,
        string? message,
        Exception? innerException)
        : base(message ?? Describe(errors), innerException)
        => Errors = errors;

    /// <summary>The failed call's errors; empty when the exception was made without any.</summary>
    public ImmutableArray<Error> Errors { get; }

    /// <summary><c>Failed transiently: Code: Message; Code: Message</c>.</summary>
    internal static string Describe(IEnumerable<Error> errors)
        => "Failed transiently: " + string.Join("; ", errors.Select(error => error.ToString()));

    private static ImmutableArray<Error> ToArray(IEnumerable<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return [.. errors];
    }
}
