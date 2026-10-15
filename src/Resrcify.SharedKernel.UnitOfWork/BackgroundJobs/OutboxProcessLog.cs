using System;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Results.Diagnostics;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// The log lines of an outbox message's failed tries, for the processing job and the lanes alike: one per failed try,
/// at <see cref="LogLevel.Warning"/> when another try may pass and at <see cref="LogLevel.Error"/> otherwise, and, for
/// the last try, one line at <see cref="LogLevel.Error"/> saying the message gave up. Each carries the event type, the message id, the try and the tries a failure of its kind gets: the
/// caller's decision (<see cref="OutboxRetryLimits.For"/>), which this only formats and logs. The exception goes
/// with its stack unless something logged it already on its way out (<see cref="LoggedExceptions"/>, e.g. the mediator's
/// unit-of-work behavior around a command the handler sent): then the line names its type and message only.
/// </summary>
internal static partial class OutboxProcessLog
{
    /// <summary>The category the lines are written under.</summary>
    public const string Category = "Resrcify.SharedKernel.UnitOfWork.Outbox";

    /// <summary>The failure kind a give-up line names when the failure was transient.</summary>
    public const string TransientFailure = "transient";

    /// <summary>The failure kind a give-up line names when the failure was not transient.</summary>
    public const string NonTransientFailure = "non-transient";

    /// <summary>Logs a failed try, or the give-up when it was the last one, and ends the exception's journey.</summary>
    /// <param name="attempt">The try that failed, 1 for the first.</param>
    /// <param name="limit">The tries the failure gets, and whether it is transient (<see cref="OutboxRetryLimits.For"/>).</param>
    public static void FailedTry(
        ILogger logger,
        string dbContext,
        string eventType,
        Guid messageId,
        int attempt,
        OutboxFailureLimit limit,
        TimeSpan? retryDelay,
        Exception exception)
    {
        var givesUp = limit.GivesUpAt(attempt);
        var level = givesUp || !limit.Transient
            ? LogLevel.Error
            : LogLevel.Warning;
        if (logger.IsEnabled(level))
        {
            // With the stack only the first time it is logged.
            var withStack = LoggedExceptions.Claim(exception) ? exception : null;
            var error = $"{exception.GetType().Name}: {exception.Message}";
            var failureKind = limit.Transient
                ? TransientFailure
                : NonTransientFailure;
            if (givesUp)
                LogGaveUp(logger, withStack, eventType, messageId, dbContext, attempt, failureKind, limit.MaxTries, error);
            else
                LogTryFailed(logger, level, withStack, eventType, messageId, dbContext, attempt, limit.MaxTries, retryDelay ?? TimeSpan.Zero, error);
        }

        // The failure is recorded for good: the same instance thrown again (a cached faulted task) is logged again.
        LoggedExceptions.Release(exception);
    }

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
