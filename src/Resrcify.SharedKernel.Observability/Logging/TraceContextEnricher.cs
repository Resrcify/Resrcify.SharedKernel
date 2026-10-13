using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace Resrcify.SharedKernel.Observability.Logging;

/// <summary>
/// Adds the trace and span the event was logged in as the <c>TraceId</c> and <c>SpanId</c> properties (the names
/// Serilog.Enrichers.Span uses), so any sink or output template can show them and a log line leads to its trace.
/// A property already set is kept.
/// </summary>
internal sealed class TraceContextEnricher
    : ILogEventEnricher
{
    public const string TraceIdProperty = "TraceId";
    public const string SpanIdProperty = "SpanId";

    public void Enrich(
        LogEvent logEvent,
        ILogEventPropertyFactory propertyFactory)
    {
        if (TraceIdOf(logEvent) is not { } traceId || SpanIdOf(logEvent) is not { } spanId)
            return;

        logEvent.AddPropertyIfAbsent(new LogEventProperty(TraceIdProperty, new ScalarValue(traceId.ToHexString())));
        logEvent.AddPropertyIfAbsent(new LogEventProperty(SpanIdProperty, new ScalarValue(spanId.ToHexString())));
    }

    // Serilog records the current activity on the event as it is created; an event made without one (by hand, or
    // before Serilog captured it) still gets the activity of the thread logging it.
    private static ActivityTraceId? TraceIdOf(
        LogEvent logEvent)
        => logEvent.TraceId is { } traceId && traceId != default
            ? traceId
            : Activity.Current?.TraceId;

    private static ActivitySpanId? SpanIdOf(
        LogEvent logEvent)
        => logEvent.SpanId is { } spanId && spanId != default
            ? spanId
            : Activity.Current?.SpanId;
}
