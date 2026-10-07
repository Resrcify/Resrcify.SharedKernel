using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;

/// <summary>Counts the outbox listener's "Waking" log lines: one per wake-up it gives the outbox.</summary>
internal sealed class WakeUpLog : ILoggerProvider
{
    private readonly ConcurrentQueue<DateTimeOffset> _wakeUps = new();

    public int Count => _wakeUps.Count;

    public DateTimeOffset[] WokenAt => [.. _wakeUps];

    public ILogger CreateLogger(string categoryName)
        => categoryName.Contains("PostgresOutboxListener", StringComparison.Ordinal)
            ? new Listener(_wakeUps)
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class Listener(ConcurrentQueue<DateTimeOffset> wakeUps) : ILogger
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
            if (formatter(state, exception).StartsWith("Waking", StringComparison.Ordinal))
                wakeUps.Enqueue(TimeProvider.System.GetUtcNow());
        }
    }

    /// <summary>The wake-ups after the first <paramref name="skip"/> (e.g. the one each connect gives).</summary>
    public int CountAfter(int skip)
        => WokenAt.Skip(skip).Count();
}
