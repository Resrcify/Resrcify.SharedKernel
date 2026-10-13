using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Resrcify.SharedKernel.Results.Diagnostics;

/// <summary>
/// The convention that makes an exception logged at <c>Error</c>, with its stack, once, however many places see it on its
/// way out: the first to log it marks it in <see cref="Exception.Data"/> under <see cref="DataKey"/>; the others see the
/// mark and log a line without the stack at <c>Debug</c>.
/// </summary>
/// <remarks>
/// <para>
/// The mediator's behaviors (the unit-of-work, transaction and logging behaviors) and the Web package's
/// <c>ResultExceptionHandler</c> follow it, so a request whose handler throws is logged at <c>Error</c> by the behavior
/// that sees it first and only at <c>Debug</c> by the exception handler that answers it with a 500. Code of a service's
/// own that logs an exception and rethrows it can follow it too: log at <c>Error</c> only when <see cref="Claim"/> is
/// <see langword="true"/>.
/// </para>
/// <para>
/// The mark is for one journey of the exception, from where it is thrown to where it is handled for good: whoever ends
/// that journey (answers it with a 500, records a job's failure, gives up on a message) calls <see cref="Release"/>
/// once it has logged. So the same instance thrown again later (a cached faulted <c>Lazy</c> or task) is logged at
/// <c>Error</c> again, once per request, not once ever. SharedKernel's own end points do; a service's own one should.
/// </para>
/// <para>
/// An exception whose <see cref="Exception.Data"/> is read-only can't be marked: every place logs it at <c>Error</c>, as
/// before.
/// </para>
/// </remarks>
public static class LoggedExceptions
{
    // Claims of one exception are made one at a time: Exception.Data is created on first use, unsynchronized, so two
    // threads seeing the exception at once could each get a dictionary of their own (and each claim it) were they to
    // lock on the dictionary. Striped by the exception's identity, so unrelated exceptions rarely wait on each other.
    private static readonly Lock[] Stripes = CreateStripes();

    /// <summary>The key under which <see cref="Exception.Data"/> holds <see langword="true"/> once it is logged.</summary>
    public const string DataKey = "Resrcify.SharedKernel.Logged";

    /// <summary>
    /// <see langword="true"/> when nobody has logged <paramref name="exception"/> yet: the caller logs it at <c>Error</c>
    /// with its stack, and it is now marked as logged. <see langword="false"/> when it is marked already: the caller logs
    /// a line without the stack, at <c>Debug</c>, or nothing.
    /// </summary>
    /// <param name="exception">The exception about to be logged.</param>
    public static bool Claim(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // The same exception can be seen on two threads at once (a task awaited twice).
        lock (StripeOf(exception))
        {
            var data = exception.Data;
            if (data.IsReadOnly)
                return true;

            if (data.Contains(DataKey))
                return false;

            data[DataKey] = true;
            return true;
        }
    }

    /// <summary>
    /// Ends the exception's journey: it is no longer marked as logged, so if the same instance is thrown again (a cached
    /// faulted task or <c>Lazy</c>, in a later request) it is logged at <c>Error</c> again. Called by whoever handles it
    /// for good, once it has logged.
    /// </summary>
    /// <param name="exception">The exception handled.</param>
    public static void Release(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        lock (StripeOf(exception))
        {
            var data = exception.Data;
            if (!data.IsReadOnly)
                data.Remove(DataKey);
        }
    }

    /// <summary>Whether <paramref name="exception"/> is marked as logged.</summary>
    /// <param name="exception">The exception.</param>
    public static bool IsLogged(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        lock (StripeOf(exception))
            return exception.Data.Contains(DataKey);
    }

    private static Lock StripeOf(Exception exception)
        => Stripes[(uint)RuntimeHelpers.GetHashCode(exception) % (uint)Stripes.Length];

    private static Lock[] CreateStripes()
    {
        var stripes = new Lock[32];
        for (var i = 0; i < stripes.Length; i++)
            stripes[i] = new Lock();
        return stripes;
    }
}
