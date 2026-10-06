using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using Shouldly;
using Xunit;
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
}
