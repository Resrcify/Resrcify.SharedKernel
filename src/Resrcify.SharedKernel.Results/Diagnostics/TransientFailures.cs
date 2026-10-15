using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.Common;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Results.Diagnostics;

/// <summary>
/// SharedKernel's one rule for whether an exception is transient (another try may get past it), and the way to turn a
/// transient failed <see cref="Result"/> into one (<see cref="ThrowIfTransient(Result)"/>).
/// </summary>
/// <remarks>
/// <para>
/// The outbox (how many tries a failed message gets), the mediator's behaviors and the jobs (the level an exception is
/// logged at) and the message bus judge an exception by this rule, through the <c>ITransientFailureClassifier</c> of the
/// Abstractions package when the container has one: that classifier adds the rules registered with
/// <c>AddTransientFailureRule</c> (e.g. the Web package's for its resilience pipeline's rejections, UnitOfWork's for a
/// concurrency conflict), and can only add transience, never take it away.
/// </para>
/// <para>
/// A cancellation the caller asked for (a shutdown, a client that gave up) isn't a failure at all: the callers check
/// their own token before they ask. So an <see cref="OperationCanceledException"/> that reaches the rule is a handler's
/// own (e.g. an HTTP call's timeout), and transient.
/// </para>
/// </remarks>
public static class TransientFailures
{
    // A cause chain is short; the bound only stops a cycle someone built by hand from looping.
    private const int MaxCauses = 64;

    /// <summary>
    /// Whether another try may get past <paramref name="exception"/>: it, or any of its causes
    /// (<see cref="Exception.InnerException"/>, and every one of an <see cref="AggregateException"/>'s
    /// <see cref="AggregateException.InnerExceptions"/>), is
    /// <list type="bullet">
    /// <item>a <see cref="TransientFailureException"/>;</item>
    /// <item>a <see cref="TimeoutException"/>, or an <see cref="OperationCanceledException"/> (a handler's own
    /// cancellation, e.g. an HTTP call's timeout; a shutdown never reaches the rule, see the remarks);</item>
    /// <item>a <see cref="DbException"/> that says it is (<see cref="DbException.IsTransient"/>);</item>
    /// <item>an <see cref="HttpRequestException"/> without a status (no answer: a refused or reset connection, a name
    /// that doesn't resolve), or with one whose error type is transient (<see cref="ErrorTypes.FromHttpStatus"/>: a 408,
    /// a 429, a 5xx).</item>
    /// </list>
    /// </summary>
    /// <param name="exception">The exception.</param>
    public static bool IsTransient(Exception exception)
        => IsTransient(exception, alsoTransient: null);

    /// <summary>
    /// Whether another try may get past <paramref name="exception"/>: the rule of <see cref="IsTransient(Exception)"/>,
    /// or <paramref name="alsoTransient"/>, for it or any of its causes. <paramref name="alsoTransient"/> can only add
    /// transience.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <param name="alsoTransient">A rule of the caller's own, applied to every exception in the chain.</param>
    public static bool IsTransient(
        Exception exception,
        Func<Exception, bool>? alsoTransient)
    {
        ArgumentNullException.ThrowIfNull(exception);

        foreach (var cause in CausesOf(exception))
        {
            if (IsTransientItself(cause))
                return true;

            if (alsoTransient?.Invoke(cause) == true)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The errors of the first <see cref="TransientFailureException"/> holding any, <paramref name="exception"/> itself or
    /// one of its causes, so whoever ends the exception's journey can answer (or record) them instead of the exception.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <param name="errors">The errors found; empty when there are none.</param>
    public static bool TryGetErrors(
        Exception exception,
        out ImmutableArray<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(exception);

        foreach (var cause in CausesOf(exception))
        {
            if (cause is TransientFailureException { Errors.IsDefaultOrEmpty: false } transient)
            {
                errors = transient.Errors;
                return true;
            }
        }

        errors = [];
        return false;
    }

    /// <summary>
    /// Throws a <see cref="TransientFailureException"/> with <paramref name="result"/>'s errors when it failed transiently
    /// (any of its errors is <see cref="ErrorTypeExtensions.IsTransient(Error)"/>), so the outbox or a job tries the work
    /// again later; returns <paramref name="result"/> otherwise (a success, or a definite failure, e.g. "not found",
    /// which another try would get the same way), for chaining.
    /// </summary>
    /// <param name="result">The call's result.</param>
    /// <exception cref="TransientFailureException">The result failed transiently.</exception>
    public static Result ThrowIfTransient(this Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsFailure && result.Errors.Any(error => error.IsTransient()))
            throw new TransientFailureException(result.Errors);

        return result;
    }

    /// <inheritdoc cref="ThrowIfTransient(Result)"/>
    public static Result<TValue> ThrowIfTransient<TValue>(this Result<TValue> result)
    {
        ThrowIfTransient((Result)result);
        return result;
    }

    /// <inheritdoc cref="ThrowIfTransient(Result)"/>
    public static async Task<Result> ThrowIfTransient(this Task<Result> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ThrowIfTransient(await result);
    }

    /// <inheritdoc cref="ThrowIfTransient(Result)"/>
    public static async Task<Result<TValue>> ThrowIfTransient<TValue>(this Task<Result<TValue>> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ThrowIfTransient(await result);
    }

    private static bool IsTransientItself(Exception exception)
        => exception switch
        {
            TransientFailureException => true,
            TimeoutException or OperationCanceledException => true,
            DbException { IsTransient: true } => true,
            HttpRequestException { StatusCode: null } => true,
            HttpRequestException { StatusCode: { } status } => ErrorTypes.FromHttpStatus((int)status).IsTransient(),
            _ => false,
        };

    // The exception, then its causes, breadth first: an aggregate's inner exceptions are all causes.
    private static IEnumerable<Exception> CausesOf(Exception exception)
    {
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var pending = new Queue<Exception>();
        pending.Enqueue(exception);
        while (pending.Count > 0 && seen.Count < MaxCauses)
        {
            var current = pending.Dequeue();
            if (!seen.Add(current))
                continue;

            yield return current;
            EnqueueCauses(current, pending);
        }
    }

    private static void EnqueueCauses(
        Exception exception,
        Queue<Exception> pending)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                pending.Enqueue(inner);
            return;
        }

        if (exception.InnerException is { } cause)
            pending.Enqueue(cause);
    }
}
