using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace Resrcify.SharedKernel.IntegrationTesting.Logging;

/// <summary>
/// Writes Serilog's events to a <see cref="FakeLogCollector"/>, as the logging abstraction's own would arrive: the
/// category is the event's <c>SourceContext</c>, the message its rendering, the state its properties (with
/// <c>{OriginalFormat}</c>), so a test reads a service logging through Serilog as one logging through any provider.
/// </summary>
internal sealed class FakeLogCollectorSink(FakeLogCollector collector)
    : ILogEventSink
{
    private const string SourceContext = "SourceContext";
    private const string OriginalFormat = "{OriginalFormat}";

    private readonly ConcurrentDictionary<string, FakeLogger> _loggers = new(StringComparer.Ordinal);

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        var category = logEvent.Properties.TryGetValue(SourceContext, out var source) && source is ScalarValue { Value: string name }
            ? name
            : string.Empty;
        var logger = _loggers.GetOrAdd(category, created => new FakeLogger(collector, created));
        var level = LevelOf(logEvent.Level);
        if (!logger.IsEnabled(level))
            return;

        var state = logEvent.Properties
            .Where(property => property.Key != SourceContext)
            .Select(property => new KeyValuePair<string, object?>(property.Key, ValueOf(property.Value)))
            .Append(new KeyValuePair<string, object?>(OriginalFormat, logEvent.MessageTemplate.Text))
            .ToList();
        var message = Render(logEvent);

        logger.Log(level, default, state, logEvent.Exception, (_, _) => message);
    }

    // As the logging abstraction renders a message: a string property as it is, not quoted as Serilog writes it.
    private static string Render(LogEvent logEvent)
    {
        var rendered = new StringBuilder();
        foreach (var token in logEvent.MessageTemplate.Tokens)
        {
            if (token is PropertyToken property && logEvent.Properties.TryGetValue(property.PropertyName, out var value))
                rendered.Append(value is ScalarValue { Value: string text }
                    ? text
                    : value.ToString(property.Format, CultureInfo.InvariantCulture));
            else
                rendered.Append(token.ToString());
        }

        return rendered.ToString();
    }

    private static object? ValueOf(LogEventPropertyValue value)
        => value is ScalarValue scalar
            ? scalar.Value
            : value.ToString(null, CultureInfo.InvariantCulture);

    private static LogLevel LevelOf(LogEventLevel level)
        => level switch
        {
            LogEventLevel.Verbose => LogLevel.Trace,
            LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Information,
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            _ => LogLevel.Critical,
        };
}
