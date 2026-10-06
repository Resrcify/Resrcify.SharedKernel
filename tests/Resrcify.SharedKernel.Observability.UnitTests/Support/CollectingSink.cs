using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Resrcify.SharedKernel.Observability.UnitTests.Support;

/// <summary>Keeps every event Serilog writes.</summary>
internal sealed class CollectingSink
    : ILogEventSink
{
    public ConcurrentQueue<LogEvent> Events { get; } = new();

    public void Emit(LogEvent logEvent)
        => Events.Enqueue(logEvent);
}
