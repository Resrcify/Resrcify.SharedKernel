using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Resrcify.SharedKernel.Observability.Logging;

/// <summary>
/// The logger <c>UseServiceRequestLogging</c> gives Serilog's request logging: the request line carries an exception's
/// stack only when nothing logged it before, following SharedKernel's logged-once convention (<c>LoggedExceptions</c>
/// in Resrcify.SharedKernel.Results: whoever logs an exception first marks it in <see cref="Exception.Data"/> under
/// <see cref="LoggedDataKey"/>, and the others log it without the stack). Followed by name, like the telemetry's
/// sources, so the package references no other SharedKernel package.
/// </summary>
/// <remarks>
/// When the request logging runs inside <c>UseExceptionHandler()</c>, it sees an unhandled exception on its way out: a
/// mediator behavior that logged it already marked it, so the line goes without it; otherwise the line is the first to
/// log it and marks it, so SharedKernel's <c>ResultExceptionHandler</c> answering the 500 logs only a <c>Debug</c> line
/// (and ends the mark). Placed before <c>UseExceptionHandler()</c>, as documented, the request logging never sees the
/// exception, and this changes nothing.
/// </remarks>
internal sealed class LoggedOnceRequestLogger
    : ILogger
{
    /// <summary>The mark of the logged-once convention: <c>LoggedExceptions.DataKey</c>.</summary>
    public const string LoggedDataKey = "Resrcify.SharedKernel.Logged";

    private readonly Func<ILogger> _target;
    private readonly bool _resolvedOnEveryWrite;

    private LoggedOnceRequestLogger(
        Func<ILogger> target,
        bool resolvedOnEveryWrite)
    {
        _target = target;
        _resolvedOnEveryWrite = resolvedOnEveryWrite;
    }

    /// <summary>
    /// Writes through <paramref name="logger"/>, or, when it is <see langword="null"/>, through the static
    /// <see cref="Log.Logger"/> of the moment, as Serilog's request logging does without a logger.
    /// </summary>
    public static ILogger Over(ILogger? logger)
        => logger is null
            ? new LoggedOnceRequestLogger(static () => Log.Logger, resolvedOnEveryWrite: true)
            : new LoggedOnceRequestLogger(() => logger, resolvedOnEveryWrite: false);

    public ILogger ForContext(ILogEventEnricher enricher)
        => Derive(target => target.ForContext(enricher));

    public ILogger ForContext(IEnumerable<ILogEventEnricher> enrichers)
        => Derive(target => target.ForContext(enrichers));

    public ILogger ForContext(
        string propertyName,
        object? value,
        bool destructureObjects = false)
        => Derive(target => target.ForContext(propertyName, value, destructureObjects));

    public ILogger ForContext<TSource>()
        => Derive(target => target.ForContext<TSource>());

    public ILogger ForContext(Type source)
        => Derive(target => target.ForContext(source));

    public bool IsEnabled(LogEventLevel level)
        => _target().IsEnabled(level);

    public bool BindMessageTemplate(
        string messageTemplate,
        object?[]? propertyValues,
        [NotNullWhen(true)] out MessageTemplate? parsedTemplate,
        [NotNullWhen(true)] out IEnumerable<LogEventProperty>? boundProperties)
        => _target().BindMessageTemplate(messageTemplate, propertyValues, out parsedTemplate, out boundProperties);

    public bool BindProperty(
        string? propertyName,
        object? value,
        bool destructureObjects,
        [NotNullWhen(true)] out LogEventProperty? property)
        => _target().BindProperty(propertyName, value, destructureObjects, out property);

    public void Write(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var written = logEvent.Exception is { } exception && !Claim(exception)
            ? WithoutException(logEvent)
            : logEvent;
        _target().Write(written);
    }

    /// <summary>
    /// <see langword="true"/> when nothing logged <paramref name="exception"/> yet: the line logs it, and it is now marked.
    /// An exception whose <see cref="Exception.Data"/> is read-only can't be marked, and is always logged.
    /// </summary>
    internal static bool Claim(Exception exception)
    {
        var data = exception.Data;
        if (data.IsReadOnly)
            return true;

        if (data.Contains(LoggedDataKey))
            return false;

        data[LoggedDataKey] = true;
        return true;
    }

    private static LogEvent WithoutException(LogEvent logEvent)
        => new(
            logEvent.Timestamp,
            logEvent.Level,
            exception: null,
            logEvent.MessageTemplate,
            logEvent.Properties.Select(property => new LogEventProperty(property.Key, property.Value)),
            logEvent.TraceId ?? default,
            logEvent.SpanId ?? default);

    // A logger for a context resolves its target again on every write when the target is the static logger (which a
    // host may replace), and once otherwise.
    private LoggedOnceRequestLogger Derive(Func<ILogger, ILogger> derive)
    {
        if (_resolvedOnEveryWrite)
            return new LoggedOnceRequestLogger(() => derive(_target()), resolvedOnEveryWrite: true);

        var derived = derive(_target());
        return new LoggedOnceRequestLogger(() => derived, resolvedOnEveryWrite: false);
    }
}
