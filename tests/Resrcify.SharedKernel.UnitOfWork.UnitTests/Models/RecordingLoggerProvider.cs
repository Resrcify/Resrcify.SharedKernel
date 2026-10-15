using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

/// <summary>Keeps every entry logged through it, from any category.</summary>
internal sealed class RecordingLoggerProvider
    : ILoggerProvider
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();

    /// <summary>What the outbox's processing logged (its failed tries and give-ups).</summary>
    public IReadOnlyList<LogEntry> Outbox()
        => [.. Entries.Where(entry => entry.Category == "Resrcify.SharedKernel.UnitOfWork.Outbox")];

    public ILogger CreateLogger(string categoryName)
        => new RecordingLogger(categoryName, Entries);

    public void Dispose()
    {
        // Nothing to release.
    }

    internal sealed record LogEntry(
        string Category,
        LogLevel Level,
        Exception? Exception,
        string Message);

    private sealed class RecordingLogger(
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
        {
            ArgumentNullException.ThrowIfNull(formatter);
            entries.Enqueue(new LogEntry(category, logLevel, exception, formatter(state, exception)));
        }
    }
}
