using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Resrcify.SharedKernel.IntegrationTesting.Waiting;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;

/// <summary>
/// Counts the outbox listener's "Waking" log lines, one per wake-up it gives the outbox, and signals when the count
/// reaches a number (<see cref="ReachedAsync"/>): a test waits for the wake-up itself, not for a time.
/// </summary>
internal sealed class WakeUpLog : ILoggerProvider
{
    private readonly CallCounter _wakeUps = new();

    /// <summary>The wake-ups so far.</summary>
    public int Count
        => _wakeUps.Count;

    /// <summary>Completes once the listener woke the outbox <paramref name="count"/> times.</summary>
    public Task ReachedAsync(int count)
        => _wakeUps.ReachedAsync(count);

    public ILogger CreateLogger(string categoryName)
        => categoryName.Contains("PostgresOutboxListener", StringComparison.Ordinal)
            ? new Listener(_wakeUps)
            : NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class Listener(CallCounter wakeUps) : ILogger
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
                wakeUps.Increment();
        }
    }
}
