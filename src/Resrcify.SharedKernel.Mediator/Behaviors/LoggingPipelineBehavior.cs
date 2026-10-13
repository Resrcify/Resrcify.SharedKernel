using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>
/// Logs each request: when it starts and completes (at <see cref="LoggingPipelineOptions.RequestLevel"/>), a failure
/// (see <see cref="FailureLevel"/>), an exception, and a request slower than
/// <see cref="LoggingPipelineOptions.SlowRequestThreshold"/>. Durations come from the <see cref="TimeProvider"/>.
/// </summary>
public class LoggingPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    private readonly ILogger<LoggingPipelineBehavior<TRequest, TResponse>> _logger;
    private readonly TimeProvider _time;
    private readonly LoggingPipelineOptions _options;

    public LoggingPipelineBehavior(
        ILogger<LoggingPipelineBehavior<TRequest, TResponse>> logger,
        TimeProvider? timeProvider = null,
        LoggingPipelineOptions? options = null)
    {
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _options = options ?? new LoggingPipelineOptions();
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var start = _time.GetUtcNow().UtcDateTime;
        var started = _time.GetTimestamp();
        if (_logger.IsEnabled(_options.RequestLevel))
        {
            _logger.Log(
                _options.RequestLevel,
                "Starting request {@RequestName}, {@DateTimeUtc}",
                typeof(TRequest).Name,
                start);
        }

        TResponse result;
        try
        {
            result = await next(cancellationToken);
        }
        catch (Exception exception) when (LogUnlessCancelled(exception, started, cancellationToken))
        {
            throw;   // never reached: the filter logs and lets the exception go on unchanged
        }

        var end = _time.GetUtcNow().UtcDateTime;
        var elapsed = _time.GetElapsedTime(started);
        var differenceMs = elapsed.TotalMilliseconds;
        var failureLevel = FailureLevel(result);
        if (result.IsFailure && _logger.IsEnabled(failureLevel))
        {
            _logger.Log(
                failureLevel,
                "Request failure {@RequestName}, {@Error}, {@DateTimeUtc} ({@DifferenceMs} ms)",
                typeof(TRequest).Name,
                result.Errors,
                end,
                differenceMs);
        }

        if (_options.SlowRequestThreshold is { } threshold && elapsed > threshold && _logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning(
                "Slow request {@RequestName} took {@DifferenceMs} ms (over {@ThresholdMs} ms)",
                typeof(TRequest).Name,
                differenceMs,
                threshold.TotalMilliseconds);
        }

        if (_logger.IsEnabled(_options.RequestLevel))
        {
            _logger.Log(
                _options.RequestLevel,
                "Completed request {@RequestName}, {@DateTimeUtc} ({@DifferenceMs} ms)",
                typeof(TRequest).Name,
                end,
                differenceMs);
        }
        return result;
    }

    /// <summary>
    /// Information for a failure that is the caller's (not found, invalid, ...): an expected answer. Warning for anything
    /// else (a failure, an upstream's failure, a timeout, a rate limit): something may be wrong.
    /// </summary>
    private static LogLevel FailureLevel(Result result)
        => result.IsFailure && result.Errors.Any(error => error.IsTransient())
            ? LogLevel.Warning
            : LogLevel.Information;

    // Logs from the exception filter, which doesn't catch: the caller gets the original exception and can handle it by
    // type. A cancellation the caller asked for isn't an error, so it isn't logged. An exception a behavior inside this
    // one (unit of work, transaction) already logged at Error is logged again only at Debug, without its stack.
    private bool LogUnlessCancelled(
        Exception exception,
        long started,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        if (_logger.IsEnabled(LogLevel.Error) && LoggedExceptions.Claim(exception))
        {
            _logger.LogError(
                exception,
                "Request {@RequestName} threw {@ExceptionType} ({@DifferenceMs} ms)",
                typeof(TRequest).Name,
                exception.GetType().Name,
                _time.GetElapsedTime(started).TotalMilliseconds);
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Request {@RequestName} threw {@ExceptionType} ({@DifferenceMs} ms), logged where it was thrown",
                typeof(TRequest).Name,
                exception.GetType().Name,
                _time.GetElapsedTime(started).TotalMilliseconds);
        }

        return false;
    }
}
