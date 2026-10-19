using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// A Quartz job that sends a command through the mediator (<see cref="ISender"/>) and reports how it went:
/// <list type="bullet">
/// <item>a failed result is logged with its errors at <see cref="LogLevel.Warning"/>, whether another try may pass
/// (<see cref="ErrorTypeExtensions.IsTransient"/>) or not (not found, invalid, ...): nobody waits for a scheduled run's
/// answer, so its failure is a warning, seen at the level production runs at (an endpoint's or the mediator's expected
/// answer stays <see cref="LogLevel.Information"/>); the trigger keeps its schedule either way;</item>
/// <item>a cancellation (the scheduler shutting down, or the job interrupted) goes through as it is;</item>
/// <item>any other exception is logged at <see cref="LogLevel.Error"/>, or at <see cref="LogLevel.Warning"/> when it is
/// transient (by the <see cref="ITransientFailureClassifier"/> given, else <see cref="TransientFailures"/>: e.g. a
/// <see cref="TransientFailureException"/>, a timeout), at <see cref="LogLevel.Debug"/> without the stack when the
/// mediator's behaviors logged it already (<see cref="LoggedExceptions"/>), and thrown again as a
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
public abstract class CommandJob<TCommand, TResponse>
    : IJob
    where TCommand : IRequest<TResponse>
    where TResponse : Result
{
    private readonly CommandJobRun<TCommand, TResponse> _run;

    /// <param name="sender">The mediator.</param>
    /// <param name="logger">The job's logger.</param>
    /// <param name="transientFailures">
    /// Tells a transient exception (logged at <see cref="LogLevel.Warning"/>) from any other; without one, the built-in
    /// rule (<see cref="TransientFailures"/>).
    /// </param>
    protected CommandJob(
        ISender sender,
        ILogger logger,
        ITransientFailureClassifier? transientFailures = null)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(logger);
        CommandJobs.EnsureReturns<TCommand, TResponse>();
        _run = new CommandJobRun<TCommand, TResponse>(sender, logger, transientFailures);
    }

    public ValueTask Execute(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _run.ExecuteAsync(
            context,
            _ => ValueTask.FromResult(CommandJobRun<TCommand, TResponse>.Send(CreateCommand(context))),
            cancellationToken);
    }

    /// <summary>The command to send this run.</summary>
    protected abstract TCommand CreateCommand(IJobExecutionContext context);
}

/// <summary>
/// A Quartz job that works out, asynchronously, whether there is a command to send this run and which, then sends it
/// through the mediator (<see cref="ISender"/>) and reports how it went, as <see cref="CommandJob{TCommand, TResponse}"/>
/// does. It is for a job that reads the database (or anything else) first and may have nothing to do: when
/// <see cref="CreateCommandAsync"/> returns <see langword="null"/>, the run is skipped, logged at
/// <see cref="LogLevel.Debug"/>, and ends normally, so it counts as a heartbeat for the <c>AddQuartzJobs</c> liveness
/// check like any other run.
/// <list type="bullet">
/// <item>a failed result is logged with its errors at <see cref="LogLevel.Warning"/>, transient or not; the trigger
/// keeps its schedule;</item>
/// <item>a cancellation (the scheduler shutting down, or the job interrupted), also while the command is made, goes
/// through as it is;</item>
/// <item>any other exception, from <see cref="CreateCommandAsync"/> too, is logged at <see cref="LogLevel.Error"/> (at
/// <see cref="LogLevel.Warning"/> when it is transient) and thrown again as a <see cref="JobExecutionException"/> (the
/// trigger keeps its schedule).</item>
/// </list>
/// Runs never overlap (<see cref="DisallowConcurrentExecutionAttribute"/>, which derived jobs inherit). Quartz builds
/// the job in a DI scope of its own for every run, so a repository or a <c>DbContext</c> can be taken in the
/// constructor. Schedule it with <c>AddIntervalJob&lt;TJob&gt;</c> or <c>AddCronJob&lt;TJob&gt;</c>.
/// </summary>
/// <example>
/// <code>
/// [DisallowConcurrentExecution]
/// internal sealed class AddPlayersJob(
///     ISender sender,
///     ILogger&lt;AddPlayersJob&gt; logger,
///     ITournamentRepository tournaments)
///     : AsyncCommandJob&lt;AddPlayersCommand&gt;(sender, logger)
/// {
///     protected override async ValueTask&lt;AddPlayersCommand?&gt; CreateCommandAsync(
///         IJobExecutionContext context,
///         CancellationToken cancellationToken)
///         =&gt; await tournaments.GetLatestTournament(cancellationToken) is { } tournament
///             ? new AddPlayersCommand(tournament.Id)
///             : null;   // nothing to do this run
/// }
/// </code>
/// </example>
/// <typeparam name="TCommand">The command sent; a reference type, so <see langword="null"/> can mean "none".</typeparam>
/// <typeparam name="TResponse">The command's result type.</typeparam>
[DisallowConcurrentExecution]
public abstract class AsyncCommandJob<TCommand, TResponse>
    : IJob
    where TCommand : class, IRequest<TResponse>
    where TResponse : Result
{
    private readonly CommandJobRun<TCommand, TResponse> _run;

    /// <param name="sender">The mediator.</param>
    /// <param name="logger">The job's logger.</param>
    /// <param name="transientFailures">
    /// Tells a transient exception (logged at <see cref="LogLevel.Warning"/>) from any other; without one, the built-in
    /// rule (<see cref="TransientFailures"/>).
    /// </param>
    protected AsyncCommandJob(
        ISender sender,
        ILogger logger,
        ITransientFailureClassifier? transientFailures = null)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(logger);
        CommandJobs.EnsureReturns<TCommand, TResponse>();
        _run = new CommandJobRun<TCommand, TResponse>(sender, logger, transientFailures);
    }

    public ValueTask Execute(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _run.ExecuteAsync(
            context,
            token => CreateAsync(context, token),
            cancellationToken);
    }

    /// <summary>
    /// The command to send this run, or <see langword="null"/> when there is nothing to send (the run is skipped and
    /// logged at <see cref="LogLevel.Debug"/>).
    /// </summary>
    protected abstract ValueTask<TCommand?> CreateCommandAsync(
        IJobExecutionContext context,
        CancellationToken cancellationToken);

    private async ValueTask<CommandJobRun<TCommand, TResponse>.CommandToSend> CreateAsync(
        IJobExecutionContext context,
        CancellationToken cancellationToken)
    {
        var command = await CreateCommandAsync(context, cancellationToken);
        return command is null
            ? CommandJobRun<TCommand, TResponse>.Skip
            : CommandJobRun<TCommand, TResponse>.Send(command);
    }
}

/// <summary>An <see cref="AsyncCommandJob{TCommand, TResponse}"/> for a command returning a <see cref="Result"/>.</summary>
/// <typeparam name="TCommand">The command sent.</typeparam>
public abstract class AsyncCommandJob<TCommand>(
    ISender sender,
    ILogger logger,
    ITransientFailureClassifier? transientFailures = null)
    : AsyncCommandJob<TCommand, Result>(sender, logger, transientFailures)
    where TCommand : class, IRequest<Result>;

/// <summary>
/// One run of a command job: makes the command, sends it and logs how it went. Shared by
/// <see cref="CommandJob{TCommand, TResponse}"/> and <see cref="AsyncCommandJob{TCommand, TResponse}"/>.
/// </summary>
internal sealed partial class CommandJobRun<TCommand, TResponse>
    where TCommand : IRequest<TResponse>
    where TResponse : Result
{
    private readonly ISender _sender;
    private readonly ILogger _logger;
    private readonly ITransientFailureClassifier? _transientFailures;

    public CommandJobRun(
        ISender sender,
        ILogger logger,
        ITransientFailureClassifier? transientFailures)
    {
        _sender = sender;
        _logger = logger;
        _transientFailures = transientFailures;
    }

    /// <summary>No command this run.</summary>
    public static CommandToSend Skip => default;

    /// <summary>Send <paramref name="command"/> this run.</summary>
    public static CommandToSend Send(TCommand command)
        => new(true, command);

    public async ValueTask ExecuteAsync(
        IJobExecutionContext context,
        Func<CancellationToken, ValueTask<CommandToSend>> createCommand,
        CancellationToken cancellationToken)
    {
        var job = context.JobDetail.Key.Name;

        TResponse response;
        try
        {
            var toSend = await createCommand(cancellationToken);
            if (!toSend.HasCommand)
            {
                LogSkippedRun(job);
                return;
            }

            response = await _sender.Send<TResponse>(toSend.Command, cancellationToken);
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

    private void LogSkippedRun(string job)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            LogSkipped(job, typeof(TCommand).Name);
    }

    // At Error (Warning when transient) with the stack unless the mediator's behaviors did so already; the run is where
    // the exception ends, so its mark is released: the same instance thrown in a later run is logged again.
    private void LogThrewOnce(
        Exception exception,
        string job)
    {
        if (LoggedExceptions.Claim(exception))
        {
            var level = IsTransient(exception)
                ? LogLevel.Warning
                : LogLevel.Error;
            LogThrew(level, exception, job, typeof(TCommand).Name);
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            var exceptionType = exception.GetType().Name;
            LogThrewLoggedAlready(job, typeof(TCommand).Name, exceptionType);
        }

        LoggedExceptions.Release(exception);
    }

    private bool IsTransient(Exception exception)
        => _transientFailures?.IsTransient(exception) ?? TransientFailures.IsTransient(exception);

    // At Warning, transient or not: a scheduled run has no caller to hand its failure to, and production logs at
    // Warning, so an expected answer (not found, invalid) at Information would leave no trace of the failed run.
    private void LogFailure(
        string job,
        TResponse response)
    {
        if (!_logger.IsEnabled(LogLevel.Warning))
            return;

        var errors = string.Join("; ", response.Errors.Select(error => $"{error.Code} ({error.Type}): {error.Message}"));
        LogFailed(job, typeof(TCommand).Name, errors);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Job {Job} had no {Command} to send this run; skipped")]
    private partial void LogSkipped(string job, string command);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {Job} sent {Command}, which failed: {Errors}")]
    private partial void LogFailed(string job, string command, string errors);

    [LoggerMessage(Message = "Job {Job} sent {Command}, which threw")]
    private partial void LogThrew(LogLevel level, Exception exception, string job, string command);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Job {Job} sent {Command}, which threw {ExceptionType} (logged already)")]
    private partial void LogThrewLoggedAlready(string job, string command, string exceptionType);

    /// <summary>Whether a run has a command to send, and which.</summary>
    internal readonly record struct CommandToSend(bool HasCommand, TCommand Command);
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
    ILogger logger,
    ITransientFailureClassifier? transientFailures = null)
    : CommandJob<TCommand, Result>(sender, logger, transientFailures)
    where TCommand : IRequest<Result>;

/// <summary>
/// Sends a new <typeparamref name="TCommand"/>, returning <typeparamref name="TResponse"/>, every run: a command job
/// without a class of its own. Schedule it with <see cref="IntervalJobSetup.AddIntervalCommandJob{TCommand, TResponse}(IQuartzBuilder, TimeSpan, TimeSpan?, TimeProvider?)"/>.
/// </summary>
/// <typeparam name="TCommand">The command sent; made with its parameterless constructor.</typeparam>
/// <typeparam name="TResponse">The command's result type, e.g. <c>Result&lt;int&gt;</c>.</typeparam>
public sealed class SendCommandJob<TCommand, TResponse>(
    ISender sender,
    ILogger<SendCommandJob<TCommand, TResponse>> logger,
    ITransientFailureClassifier? transientFailures = null)
    : CommandJob<TCommand, TResponse>(sender, logger, transientFailures)
    where TCommand : IRequest<TResponse>, new()
    where TResponse : Result
{
    protected override TCommand CreateCommand(IJobExecutionContext context)
        => new();
}

/// <summary>
/// Sends a new <typeparamref name="TCommand"/> every run: a command job without a class of its own. Schedule it with
/// <see cref="IntervalJobSetup.AddIntervalCommandJob{TCommand}(IQuartzBuilder, TimeSpan, TimeSpan?, TimeProvider?)"/>.
/// </summary>
/// <typeparam name="TCommand">The command sent; made with its parameterless constructor.</typeparam>
public sealed class SendCommandJob<TCommand>(
    ISender sender,
    ILogger<SendCommandJob<TCommand>> logger,
    ITransientFailureClassifier? transientFailures = null)
    : CommandJob<TCommand>(sender, logger, transientFailures)
    where TCommand : IRequest<Result>, new()
{
    protected override TCommand CreateCommand(IJobExecutionContext context)
        => new();
}

/// <summary>
/// Sends the <typeparamref name="TCommand"/> its <see cref="CommandFactory{TCommand}"/> makes from the run's DI scope,
/// returning <typeparamref name="TResponse"/>, every run: the job of the <c>AddIntervalCommandJob</c> /
/// <c>AddCronCommandJob</c> overloads taking a <c>createCommand</c>, for a command without a parameterless constructor.
/// </summary>
/// <typeparam name="TCommand">The command sent.</typeparam>
/// <typeparam name="TResponse">The command's result type.</typeparam>
internal sealed class FactoryCommandJob<TCommand, TResponse>(
    ISender sender,
    ILogger<FactoryCommandJob<TCommand, TResponse>> logger,
    CommandFactory<TCommand> factory,
    IServiceProvider services,
    ITransientFailureClassifier? transientFailures = null)
    : CommandJob<TCommand, TResponse>(sender, logger, transientFailures)
    where TCommand : IRequest<TResponse>
    where TResponse : Result
{
    protected override TCommand CreateCommand(IJobExecutionContext context)
        => factory.Create(services);
}
