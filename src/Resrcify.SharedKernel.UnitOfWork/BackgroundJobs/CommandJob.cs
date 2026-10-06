using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// A Quartz job that sends a command through the mediator (<see cref="ISender"/>) and reports how it went:
/// <list type="bullet">
/// <item>a failed result is logged with its errors, at <see cref="LogLevel.Warning"/> when another try may pass
/// (<see cref="ErrorTypeExtensions.IsTransient"/>) and at <see cref="LogLevel.Information"/> when it is an expected
/// answer (not found, invalid, ...); the trigger keeps its schedule either way;</item>
/// <item>a cancellation (the scheduler shutting down, or the job interrupted) goes through as it is;</item>
/// <item>any other exception is logged at <see cref="LogLevel.Error"/> and thrown again as a
/// <see cref="JobExecutionException"/>, the exception Quartz expects from a job: listeners and Quartz's own telemetry
/// see the run failed, and the trigger keeps its schedule (no refire, no unscheduling).</item>
/// </list>
/// Runs never overlap (<see cref="DisallowConcurrentExecutionAttribute"/>, which derived jobs inherit).
/// </summary>
/// <typeparam name="TCommand">The command sent.</typeparam>
/// <typeparam name="TResponse">The command's result type.</typeparam>
[DisallowConcurrentExecution]
public abstract partial class CommandJob<TCommand, TResponse>
    : IJob
    where TCommand : IRequest<TResponse>
    where TResponse : Result
{
    private readonly ISender _sender;
    private readonly ILogger _logger;

    protected CommandJob(
        ISender sender,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(logger);
        _sender = sender;
        _logger = logger;
    }

    public async ValueTask Execute(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var job = context.JobDetail.Key.Name;

        TResponse response;
        try
        {
            response = await _sender.Send<TResponse>(CreateCommand(context), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogThrew(exception, job, typeof(TCommand).Name);
            throw new JobExecutionException(exception);
        }

        if (response.IsFailure)
            LogFailure(job, response);
    }

    /// <summary>The command to send this run.</summary>
    protected abstract TCommand CreateCommand(IJobExecutionContext context);

    private void LogFailure(
        string job,
        TResponse response)
    {
        var level = response.Errors.Any(error => error.Type.IsTransient())
            ? LogLevel.Warning
            : LogLevel.Information;
        if (!_logger.IsEnabled(level))
            return;

        var errors = string.Join("; ", response.Errors.Select(error => $"{error.Code} ({error.Type}): {error.Message}"));
        LogFailed(level, job, typeof(TCommand).Name, errors);
    }

    [LoggerMessage(Message = "Job {Job} sent {Command}, which failed: {Errors}")]
    private partial void LogFailed(LogLevel level, string job, string command, string errors);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Job} sent {Command}, which threw")]
    private partial void LogThrew(Exception exception, string job, string command);
}

/// <summary>A <see cref="CommandJob{TCommand, TResponse}"/> for a command returning a <see cref="Result"/>.</summary>
/// <typeparam name="TCommand">The command sent.</typeparam>
public abstract class CommandJob<TCommand>(
    ISender sender,
    ILogger logger)
    : CommandJob<TCommand, Result>(sender, logger)
    where TCommand : IRequest<Result>;

/// <summary>
/// Sends a new <typeparamref name="TCommand"/> every run: a command job without a class of its own. Schedule it with
/// <see cref="IntervalJobSetup.AddIntervalCommandJob{TCommand}"/>.
/// </summary>
/// <typeparam name="TCommand">The command sent; made with its parameterless constructor.</typeparam>
public sealed class SendCommandJob<TCommand>(
    ISender sender,
    ILogger<SendCommandJob<TCommand>> logger)
    : CommandJob<TCommand>(sender, logger)
    where TCommand : IRequest<Result>, new()
{
    protected override TCommand CreateCommand(IJobExecutionContext context)
        => new();
}
