using System;
using System.Collections;

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
/// An exception whose <see cref="Exception.Data"/> is read-only can't be marked: every place logs it at <c>Error</c>, as
/// before.
/// </para>
/// </remarks>
public static class LoggedExceptions
{
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

        var data = exception.Data;
        if (data.IsReadOnly)
            return true;

        // The same exception can be seen on two threads at once (a task awaited twice).
        lock (((ICollection)data).SyncRoot)
        {
            if (data.Contains(DataKey))
                return false;

            data[DataKey] = true;
            return true;
        }
    }

    /// <summary>Whether <paramref name="exception"/> is marked as logged.</summary>
    /// <param name="exception">The exception.</param>
    public static bool IsLogged(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data.Contains(DataKey);
    }
}
