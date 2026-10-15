using System;
using System.Data.Common;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.UnitOfWork.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// The log lines of an outbox message's failed tries, for the processing job and the lanes alike: one per failed try,
/// at <see cref="LogLevel.Warning"/> when another try may pass (<see cref="IsTransient"/>) and at
/// <see cref="LogLevel.Error"/> otherwise, and, for the last try, one line at <see cref="LogLevel.Error"/> saying the
/// message gave up. Each carries the event type, the message id, the try and the tries a failure of its kind gets
/// (<see cref="OutboxRetryLimits"/>: a transient one more than any other). The exception goes
/// with its stack unless something logged it already on its way out (<see cref="LoggedExceptions"/>, e.g. the mediator's
/// unit-of-work behavior around a command the handler sent): then the line names its type and message only.
/// </summary>
internal static partial class OutboxProcessLog
{
    /// <summary>The category the lines are written under.</summary>
    public const string Category = "Resrcify.SharedKernel.UnitOfWork.Outbox";

    // Polly's exceptions, by name: UnitOfWork doesn't reference Polly.
    private const string PollyTimeout = "TimeoutRejectedException";
    private const string PollyOpenCircuit = "BrokenCircuitException";

    /// <summary>The failure kind a give-up line names when the failure was transient.</summary>
    public const string TransientFailure = "transient";

    /// <summary>The failure kind a give-up line names when the failure was not transient.</summary>
    public const string NonTransientFailure = "non-transient";

    /// <summary>Logs a failed try, or the give-up when it was the last one, and ends the exception's journey.</summary>
    /// <param name="maxRetryCount">The tries a failure of this kind gets (<see cref="OutboxRetryLimits"/>).</param>
    /// <param name="transient">Whether the failure is transient (<see cref="IsTransient"/>).</param>
    public static void FailedTry(
        ILogger logger,
        string dbContext,
        string eventType,
        Guid messageId,
        int attempt,
        int maxRetryCount,
        bool transient,
        TimeSpan? retryDelay,
        Exception exception)
    {
        var givesUp = attempt >= maxRetryCount;
        var level = givesUp || !transient
            ? LogLevel.Error
            : LogLevel.Warning;
        if (logger.IsEnabled(level))
        {
            // With the stack only the first time it is logged.
            var withStack = LoggedExceptions.Claim(exception) ? exception : null;
            var error = $"{exception.GetType().Name}: {exception.Message}";
            var failureKind = transient
                ? TransientFailure
                : NonTransientFailure;
            if (givesUp)
                LogGaveUp(logger, withStack, eventType, messageId, dbContext, attempt, failureKind, maxRetryCount, error);
            else
                LogTryFailed(logger, level, withStack, eventType, messageId, dbContext, attempt, maxRetryCount, retryDelay ?? TimeSpan.Zero, error);
        }

        // The failure is recorded for good: the same instance thrown again (a cached faulted task) is logged again.
        LoggedExceptions.Release(exception);
    }

    /// <summary>
    /// Whether another try may pass: a cancellation or a timeout (the handler's own, e.g. an HTTP call's), a transient
    /// database failure (a deadlock, a serialization failure, a concurrency conflict, <see cref="DbException.IsTransient"/>),
    /// an HTTP request that got no answer or a 408, 429 or 5xx, or Polly's timeout or open circuit; anywhere among the
    /// exception's causes.
    /// </summary>
    internal static bool IsTransient(Exception exception)
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

    [LoggerMessage(
        Message = "Outbox message {MessageId} ({EventType}, {DbContext}) failed try {Attempt} of {MaxRetryCount}; "
            + "the next try waits {RetryDelay}: {Error}")]
    private static partial void LogTryFailed(
        ILogger logger,
        LogLevel level,
        Exception? exception,
        string eventType,
        Guid messageId,
        string dbContext,
        int attempt,
        int maxRetryCount,
        TimeSpan retryDelay,
        string error);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Outbox message {MessageId} ({EventType}, {DbContext}) gave up after try {Attempt} ({FailureKind} "
            + "failures get {MaxRetryCount} tries); it is kept as poison: {Error}")]
    private static partial void LogGaveUp(
        ILogger logger,
        Exception? exception,
        string eventType,
        Guid messageId,
        string dbContext,
        int attempt,
        string failureKind,
        int maxRetryCount,
        string error);
}
