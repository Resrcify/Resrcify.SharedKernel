using System;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Registers a job that runs on a cron schedule on a Quartz scheduler: the job, its key (from its type, as
/// <see cref="IntervalJobSetup"/> gives it) and a cron trigger, in one call instead of a <c>*JobSetup</c> class per job.
/// The cron counterpart of <see cref="IntervalJobSetup"/>.
/// </summary>
/// <example>
/// <code>
/// services.AddQuartz(quartz => quartz
///     .AddJobHeartbeats()
///     .AddCronJob&lt;UpdateShardsWithRotatingPayoutsJob&gt;("0 0/15 * * * ?")
///     .AddCronCommandJob&lt;ProcessUnitChangesCommand&gt;("0 0 16 * * ?"));
/// </code>
/// </example>
public static class CronJobSetup
{
    /// <summary>
    /// Runs <typeparamref name="TJob"/> whenever <paramref name="cronExpression"/> fires, in <paramref name="timeZone"/>
    /// (UTC by default, so a schedule doesn't move with the host's zone or its daylight saving time). Runs never overlap:
    /// a run that outlasts the gap to the next fire holds it back until it ends (as
    /// <see cref="DisallowConcurrentExecutionAttribute"/> does; mark the job with it anyway). A fire missed while the
    /// scheduler was busy or down fires once as soon as it can (Quartz's default for cron triggers). The job's key is
    /// <see cref="IntervalJobSetup.JobKeyFor{TJob}"/>, and the <c>AddQuartzJobs</c> liveness check judges it by the fire
    /// that is due next (a nightly job isn't stalled at noon).
    /// </summary>
    /// <param name="quartz">The Quartz builder.</param>
    /// <param name="cronExpression">
    /// A Quartz cron expression, with seconds: <c>"0 0/15 * * * ?"</c> (every 15 minutes), <c>"0 0 16 * * ?"</c> (16:00
    /// every day). Checked here: one that doesn't parse, or never fires, throws an <see cref="ArgumentException"/>.
    /// </param>
    /// <param name="timeZone">The zone the expression is read in; <see cref="TimeZoneInfo.Utc"/> when <see langword="null"/>.</param>
    /// <param name="timeProvider">
    /// The clock the trigger's start is read from (the first fire is the first one after it); the
    /// <see cref="TimeProvider"/> registered in DI when <see langword="null"/> (the system clock when none is). The
    /// scheduler itself runs on the registered one (Quartz 4).
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="cronExpression"/> doesn't parse, or never fires.</exception>
    public static IQuartzBuilder AddCronJob<TJob>(
        this IQuartzBuilder quartz,
        string cronExpression,
        TimeZoneInfo? timeZone = null,
        TimeProvider? timeProvider = null)
        where TJob : class, IJob
    {
        ArgumentNullException.ThrowIfNull(quartz);
        var zone = timeZone ?? TimeZoneInfo.Utc;
        EnsureFires(cronExpression, zone);

        var jobKey = IntervalJobSetup.JobKeyFor<TJob>();
        return quartz
            .AddJob<TJob>(job => job
                .WithIdentity(jobKey)
                .DisallowConcurrentExecution())
            .AddTrigger<TJob>((provider, trigger) => trigger
                .ForJob(jobKey)
                .StartAt(Clock(provider, timeProvider).GetUtcNow())
                .WithSchedule(CronScheduleBuilder
                    .Create(cronExpression)
                    .InTimeZone(zone)));
    }

    /// <summary>
    /// Sends a new <typeparamref name="TCommand"/> through the mediator whenever <paramref name="cronExpression"/> fires,
    /// with <see cref="SendCommandJob{TCommand}"/>: no job class to write. Its key is
    /// <c>SendCommandJob-&lt;command type name&gt;</c>, as <see cref="IntervalJobSetup.AddIntervalCommandJob{TCommand}"/>
    /// gives it.
    /// </summary>
    /// <inheritdoc cref="AddCronJob{TJob}" path="/param"/>
    /// <exception cref="ArgumentException"><paramref name="cronExpression"/> doesn't parse, or never fires.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TCommand"/> returns a value (<c>Result&lt;T&gt;</c>): use
    /// <see cref="AddCronCommandJob{TCommand, TResponse}"/>.
    /// </exception>
    public static IQuartzBuilder AddCronCommandJob<TCommand>(
        this IQuartzBuilder quartz,
        string cronExpression,
        TimeZoneInfo? timeZone = null,
        TimeProvider? timeProvider = null)
        where TCommand : IRequest<Result>, new()
    {
        CommandJobs.EnsureReturns<TCommand, Result>();
        return quartz.AddCronJob<SendCommandJob<TCommand>>(
            cronExpression,
            timeZone,
            timeProvider);
    }

    /// <summary>
    /// Sends a new <typeparamref name="TCommand"/> returning <typeparamref name="TResponse"/> (e.g. an
    /// <c>ICommand&lt;int&gt;</c>, whose result is <c>Result&lt;int&gt;</c>) through the mediator whenever
    /// <paramref name="cronExpression"/> fires, with <see cref="SendCommandJob{TCommand, TResponse}"/>.
    /// </summary>
    /// <inheritdoc cref="AddCronJob{TJob}" path="/param"/>
    /// <exception cref="ArgumentException"><paramref name="cronExpression"/> doesn't parse, or never fires.</exception>
    public static IQuartzBuilder AddCronCommandJob<TCommand, TResponse>(
        this IQuartzBuilder quartz,
        string cronExpression,
        TimeZoneInfo? timeZone = null,
        TimeProvider? timeProvider = null)
        where TCommand : IRequest<TResponse>, new()
        where TResponse : Result
    {
        CommandJobs.EnsureReturns<TCommand, TResponse>();
        return quartz.AddCronJob<SendCommandJob<TCommand, TResponse>>(
            cronExpression,
            timeZone,
            timeProvider);
    }

    // At registration, not when the scheduler is built at start-up (where Quartz would throw a FormatException), and
    // a schedule that never fires (31 February) would sit there silently.
    private static void EnsureFires(
        string cronExpression,
        TimeZoneInfo timeZone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        CronExpression expression;
        try
        {
            expression = new CronExpression(cronExpression, timeZone);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                $"\"{cronExpression}\" isn't a Quartz cron expression ({exception.Message}). Quartz's has seconds first: "
                + "\"0 0/15 * * * ?\" is every 15 minutes.",
                nameof(cronExpression),
                exception);
        }

        if (expression.GetNextValidTimeAfter(DateTimeOffset.UnixEpoch) is null)
            throw new ArgumentException(
                $"\"{cronExpression}\" never fires.",
                nameof(cronExpression));
    }

    private static TimeProvider Clock(
        IServiceProvider provider,
        TimeProvider? timeProvider)
        => timeProvider
            ?? provider.GetService<TimeProvider>()
            ?? TimeProvider.System;
}
