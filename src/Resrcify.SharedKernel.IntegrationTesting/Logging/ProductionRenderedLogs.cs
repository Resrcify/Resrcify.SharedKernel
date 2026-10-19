using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Resrcify.SharedKernel.IntegrationTesting.Logging;

/// <summary>
/// Each line the host logs through Serilog as production's console prints its message: the event (an <c>ILogger</c>
/// line reaches Serilog through <c>AddServiceTelemetry</c>'s provider) formatted by the formatter the services'
/// <c>appsettings.Production.json</c> configures (<see cref="RenderedCompactJsonFormatter"/>), its rendered message
/// (<c>@m</c>). Serilog quotes a string property there (<c>failed "0"</c>) unless the template says otherwise
/// (<c>{Name:l}</c>), which the logging abstraction's rendering (<c>Logs</c>) never shows. A sink registered in the
/// container, which the host's Serilog reads (<c>ReadFrom.Services</c>).
/// </summary>
public sealed class ProductionRenderedLogs
    : ILogEventSink
{
    private readonly ConcurrentQueue<ProductionRenderedLine> _lines = new();
    private readonly RenderedCompactJsonFormatter _formatter = new();

    /// <summary>The lines so far, oldest first.</summary>
    public IReadOnlyList<ProductionRenderedLine> Lines
        => [.. _lines];

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        _formatter.Format(logEvent, writer);
        using var line = JsonDocument.Parse(writer.ToString());
        _lines.Enqueue(new ProductionRenderedLine(
            logEvent.Level,
            logEvent.MessageTemplate.Text,
            line.RootElement.GetProperty("@m").GetString() ?? string.Empty));
    }

    /// <summary>Forgets the lines so far (between the tests of a shared fixture).</summary>
    public void Clear()
        => _lines.Clear();
}
