using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

/// <summary>A job that does nothing, for scheduling tests.</summary>
internal sealed class NoopJob : IJob
{
    public ValueTask Execute(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

/// <summary>A generic job, for the keys generic jobs get.</summary>
internal sealed class GenericJob<T> : IJob
{
    public static Type Argument => typeof(T);

    public ValueTask Execute(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

/// <summary>A command a job sends.</summary>
internal sealed record PingCommand : ICommand;

/// <summary>A logger that keeps what it was asked to log.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

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
        Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
