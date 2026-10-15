using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Support;

/// <summary>Keeps every line logged through it, with its category, level and exception.</summary>
internal sealed class CapturingLoggerProvider
    : ILoggerProvider
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName)
        => new CapturingLogger(categoryName, Entries);

    public void Dispose()
    {
    }

    internal sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger(
        string category,
        ConcurrentQueue<LogEntry> entries)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception));
    }
}
