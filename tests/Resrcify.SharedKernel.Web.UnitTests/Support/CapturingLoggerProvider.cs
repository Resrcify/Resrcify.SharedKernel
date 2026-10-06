using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Resrcify.SharedKernel.Web.UnitTests.Support;

/// <summary>Keeps every log entry written through it, from any category.</summary>
internal sealed class CapturingLoggerProvider
    : ILoggerProvider
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName)
        => new CapturingLogger(categoryName, Entries);

    public void Dispose()
    {
        // Nothing to release.
    }

    internal sealed record LogEntry(
        string Category,
        LogLevel Level,
        Exception? Exception,
        string Message);

    private sealed class CapturingLogger(
        string category,
        ConcurrentQueue<LogEntry> entries)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel)
            => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => entries.Enqueue(new LogEntry(category, logLevel, exception, formatter(state, exception)));
    }
}
