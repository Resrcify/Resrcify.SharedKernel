using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using Resrcify.SharedKernel.MessageBus.Diagnostics;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Support;

/// <summary>
/// Records the message bus' counters as <c>"name tag=value …"</c>, tags sorted, for tests to look for. Every test in the
/// process shares the meter, so look for what the test itself caused (e.g. tagged with its own queue).
/// </summary>
internal sealed class MetricsCapture : IDisposable
{
    private readonly MeterListener _listener;

    public MetricsCapture()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MessageBusDiagnostics.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Record(instrument, tags));
        _listener.Start();
    }

    public ConcurrentQueue<string> Measurements { get; } = new();

    public int Count(string measurement)
        => Measurements.Count(recorded => recorded == measurement);

    public void Dispose()
        => _listener.Dispose();

    private void Record(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var rendered = tags.ToArray()
            .OrderBy(tag => tag.Key, StringComparer.Ordinal)
            .Select(tag => $" {tag.Key}={tag.Value}");
        Measurements.Enqueue(instrument.Name + string.Concat(rendered));
    }
}
