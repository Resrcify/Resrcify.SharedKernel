using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Resrcify.SharedKernel.Observability.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Observability.UnitTests.Logging;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class TraceContextEnricherTests
{
    [Fact]
    public void Enrich_ShouldAddTheTraceAndSpanTheEventWasLoggedIn()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        var logEvent = Event(traceId, spanId);

        new TraceContextEnricher().Enrich(logEvent, new PropertyFactory());

        ScalarOf(logEvent, "TraceId").ShouldBe(traceId.ToHexString());
        ScalarOf(logEvent, "SpanId").ShouldBe(spanId.ToHexString());
    }

    [Fact]
    public void Enrich_ShouldUseTheCurrentActivity_WhenTheEventHasNoTrace()
    {
        using var activity = new Activity("operation");
        activity.Start();
        var logEvent = Event(traceId: null, spanId: null);

        new TraceContextEnricher().Enrich(logEvent, new PropertyFactory());

        ScalarOf(logEvent, "TraceId").ShouldBe(activity.TraceId.ToHexString());
        ScalarOf(logEvent, "SpanId").ShouldBe(activity.SpanId.ToHexString());
    }

    [Fact]
    public void Enrich_ShouldAddNothing_WhenThereIsNoTrace()
    {
        Activity.Current = null;
        var logEvent = Event(traceId: null, spanId: null);

        new TraceContextEnricher().Enrich(logEvent, new PropertyFactory());

        logEvent.Properties.ShouldBeEmpty();
    }

    [Fact]
    public void Enrich_ShouldKeepATraceIdAlreadySet()
    {
        var logEvent = Event(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom());
        logEvent.AddOrUpdateProperty(new LogEventProperty("TraceId", new ScalarValue("from-elsewhere")));

        new TraceContextEnricher().Enrich(logEvent, new PropertyFactory());

        ScalarOf(logEvent, "TraceId").ShouldBe("from-elsewhere");
    }

    private static LogEvent Event(
        ActivityTraceId? traceId,
        ActivitySpanId? spanId)
        => new(
            DateTimeOffset.UnixEpoch,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplateParser().Parse("Something happened"),
            Enumerable.Empty<LogEventProperty>(),
            traceId ?? default,
            spanId ?? default);

    private static object? ScalarOf(
        LogEvent logEvent,
        string property)
        => ((ScalarValue)logEvent.Properties[property]).Value;

    private sealed class PropertyFactory
        : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(
            string name,
            object? value,
            bool destructureObjects = false)
            => new(name, new ScalarValue(value));
    }
}
