using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime.CompilerServices;
using Shouldly;
using Xunit;
using Resrcify.SharedKernel.Abstractions.Metrics;
using Resrcify.SharedKernel.MessageBus.Diagnostics;
using Resrcify.SharedKernel.MessageBus.ScatterGather;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Diagnostics;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageBusDiagnosticsTests
{
    [Fact]
    public void RecordReply_ShouldCountRepliesByOutcome_WhenAMeterListenerSubscribes()
    {
        var outcomes = new ConcurrentBag<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == MessageBusDiagnostics.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "outcome")
                    outcomes.Add((string)tag.Value!);
        });
        listener.Start();

        MessageBusDiagnostics.RecordReply(ReplyOutcome.Accepted);
        MessageBusDiagnostics.RecordReply(ReplyOutcome.Duplicate);
        MessageBusDiagnostics.RecordReply(ReplyOutcome.Late);

        outcomes.Order().ShouldBe(["accepted", "duplicate", "late"]);
    }

    [Theory]
    [InlineData("messagebus.scatter_gather.batch.duration")]
    [InlineData("messagebus.requests.duration")]
    [InlineData("messagebus.rate_limiter.wait")]
    [InlineData("messagebus.events.duration")]
    public void DurationHistogram_ShouldBucketBySeconds_WhenAMeterListenerSubscribes(
        string histogram)
    {
        var boundaries = AdvisedBoundaries(typeof(MessageBusDiagnostics), MessageBusDiagnostics.MeterName);

        boundaries[histogram].ShouldBe(DurationBuckets.Seconds);
    }

    private static Dictionary<string, IReadOnlyList<double>?> AdvisedBoundaries(
        Type diagnostics,
        string meterName)
    {
        RuntimeHelpers.RunClassConstructor(diagnostics.TypeHandle);
        var boundaries = new Dictionary<string, IReadOnlyList<double>?>(StringComparer.Ordinal);
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name == meterName && instrument is Histogram<double> histogram)
                    boundaries[histogram.Name] = histogram.Advice?.HistogramBucketBoundaries;
            },
        };
        listener.Start();
        return boundaries;
    }
}
