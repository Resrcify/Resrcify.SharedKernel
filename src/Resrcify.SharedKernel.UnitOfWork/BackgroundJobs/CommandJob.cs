using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// A Quartz job that sends a command through the mediator (<see cref="ISender"/>) and reports how it went:
/// <list type="bullet">
/// <item>a failed result is logged with its errors, at <see cref="LogLevel.Warning"/> when another try may pass
/// (<see cref="ErrorTypeExtensions.IsTransient"/>) and at <see cref="LogLevel.Information"/> when it is an expected
/// answer (not found, invalid, ...); the trigger keeps its schedule either way;</item>
/// <item>a cancellation (the scheduler shutting down, or the job interrupted) goes through as it is;</item>
/// <item>any other exception is logged at <see cref="LogLevel.Error"/> (at <see cref="LogLevel.Debug"/>, without the
/// stack, when the mediator's behaviors logged it already: <see cref="LoggedExceptions"/>) and thrown again as a
/// <see cref="JobExecutionException"/>, the exception Quartz expects from a job: listeners and Quartz's own telemetry
/// see the run failed, and the trigger keeps its schedule (no refire, no unscheduling).</item>
/// </list>
/// Runs never overlap (<see cref="DisallowConcurrentExecutionAttribute"/>, which derived jobs inherit).
/// <typeparamref name="TResponse"/> must be the command's own result type: a command returning <c>Result&lt;int&gt;</c>
/// also counts as an <c>IRequest&lt;Result&gt;</c> (the interface is covariant), but the mediator has no handler for
/// it as one, so the job refuses it when made.
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
        CommandJobs.EnsureReturns<TCommand, TResponse>();
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
            LogThrewOnce(exception, job);
            throw new JobExecutionException(exception);
        }

        if (response.IsFailure)
            LogFailure(job, response);
    }

    /// <summary>The command to send this run.</summary>
    protected abstract TCommand CreateCommand(IJobExecutionContext context);

    // At Error with the stack unless the mediator's behaviors did so already; the run is where the exception ends, so
    // its mark is released: the same instance thrown in a later run is logged again.
    private void LogThrewOnce(
        Exception exception,
        string job)
    {
        if (LoggedExceptions.Claim(exception))
        {
            LogThrew(exception, job, typeof(TCommand).Name);
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            var exceptionType = exception.GetType().Name;
            LogThrewLoggedAlready(job, typeof(TCommand).Name, exceptionType);
        }

        LoggedExceptions.Release(exception);
    }

    private void LogFailure(
        string job,
        TResponse response)
    {
        var level = response.Errors.Any(error => error.IsTransient())
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Job {Job} sent {Command}, which threw {ExceptionType} (logged already)")]
    private partial void LogThrewLoggedAlready(string job, string command, string exceptionType);
}

/// <summary>Checks shared by the command jobs and their registration.</summary>
internal static class CommandJobs
{
    /// <summary>
    /// Throws when <typeparamref name="TCommand"/> isn't an <c>IRequest&lt;TResponse&gt;</c> of its own, only through
    /// variance (an <c>ICommand&lt;int&gt;</c> passing for an <c>IRequest&lt;Result&gt;</c>): the mediator would find no
    /// handler for it on every run.
    /// </summary>
    public static void EnsureReturns<TCommand, TResponse>()
        where TCommand : IRequest<TResponse>
        where TResponse : Result
    {
        if (typeof(TCommand).GetInterfaces().Contains(typeof(IRequest<TResponse>)))
            return;

        var actual = typeof(TCommand)
            .GetInterfaces()
            .FirstOrDefault(implemented => implemented.IsGenericType
                && implemented.GetGenericTypeDefinition() == typeof(IRequest<>))?
            .GenericTypeArguments[0];
        var command = typeof(TCommand).Name;
        var returns = Describe(actual);
        throw new InvalidOperationException(
            $"{command} returns {returns}, not {Describe(typeof(TResponse))}: send it as such "
            + $"(CommandJob<{command}, {returns}>, or AddIntervalCommandJob<{command}, {returns}>), "
            + "or the mediator finds no handler for it.");
    }

    private static string Describe(Type? type)
    {
        if (type is null)
            return "nothing";
        if (!type.IsGenericType)
            return type.Name;

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GenericTypeArguments.Select(Describe))}>";
    }
}

/// <summary>A <see cref="CommandJob{TCommand, TResponse}"/> for a command returning a <see cref="Result"/>.</summary>
/// <typeparam name="TCommand">The command sent.</typeparam>
public abstract class CommandJob<TCommand>(
    ISender sender,
    ILogger logger)
    : CommandJob<TCommand, Result>(sender, logger)
    where TCommand : IRequest<Result>;

/// <summary>
/// Sends a new <typeparamref name="TCommand"/>, returning <typeparamref name="TResponse"/>, every run: a command job
/// without a class of its own. Schedule it with <see cref="IntervalJobSetup.AddIntervalCommandJob{TCommand, TResponse}"/>.
/// </summary>
/// <typeparam name="TCommand">The command sent; made with its parameterless constructor.</typeparam>
/// <typeparam name="TResponse">The command's result type, e.g. <c>Result&lt;int&gt;</c>.</typeparam>
public sealed class SendCommandJob<TCommand, TResponse>(
    ISender sender,
    ILogger<SendCommandJob<TCommand, TResponse>> logger)
    : CommandJob<TCommand, TResponse>(sender, logger)
    where TCommand : IRequest<TResponse>, new()
    where TResponse : Result
{
    protected override TCommand CreateCommand(IJobExecutionContext context)
        => new();
}

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
