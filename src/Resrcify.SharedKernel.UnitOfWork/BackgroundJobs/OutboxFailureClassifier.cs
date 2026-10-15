using System;
using System.Data.Common;
using System.Net;
using System.Net.Http;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.UnitOfWork.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Tells whether an outbox message's failed try may pass another time (<see cref="IsTransient"/>), which decides the
/// tries the failure gets (<see cref="OutboxRetryLimits.For"/>).
/// </summary>
internal static class OutboxFailureClassifier
{
    // Polly's exceptions, by name: UnitOfWork doesn't reference Polly.
    private const string PollyTimeout = "TimeoutRejectedException";
    private const string PollyOpenCircuit = "BrokenCircuitException";

    /// <summary>
    /// Whether another try may pass: a cancellation or a timeout (the handler's own, e.g. an HTTP call's), a transient
    /// database failure (a deadlock, a serialization failure, a concurrency conflict, <see cref="DbException.IsTransient"/>),
    /// an HTTP request that got no answer or a 408, 429 or 5xx, or Polly's timeout or open circuit; anywhere among the
    /// exception's causes.
    /// </summary>
    public static bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (IsTransientItself(current))
                return true;
        }

        return PersistenceErrors.TryGetError(exception, inTransaction: false, out var error) && error.IsTransient();
    }

    private static bool IsTransientItself(Exception exception)
        => exception switch
        {
            OperationCanceledException or TimeoutException => true,
            DbException { IsTransient: true } => true,
            HttpRequestException { StatusCode: null } => true,
            HttpRequestException { StatusCode: { } status } => IsTransientStatus(status),
            _ => exception.GetType().Name is PollyTimeout or PollyOpenCircuit,
        };

    private static bool IsTransientStatus(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;
}
