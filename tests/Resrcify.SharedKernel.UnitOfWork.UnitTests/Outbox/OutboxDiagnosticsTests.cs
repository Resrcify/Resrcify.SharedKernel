using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Resrcify.SharedKernel.Abstractions.Metrics;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Outbox;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxDiagnosticsTests
{
    [Theory]
    [InlineData("outbox.message.duration")]
    [InlineData("outbox.message.wait")]
    public void DurationHistogram_ShouldBucketBySeconds_WhenAMeterListenerSubscribes(
        string histogram)
    {
        var boundaries = AdvisedBoundaries(typeof(OutboxDiagnostics), OutboxDiagnostics.MeterName);

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
