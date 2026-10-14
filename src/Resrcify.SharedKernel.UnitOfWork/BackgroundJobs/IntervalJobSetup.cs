using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Registers a job that runs every so often on a Quartz scheduler: the job, its key (from its type) and a repeating
/// trigger, in one call instead of a <c>*JobSetup</c> class per job.
/// </summary>
/// <example>
/// <code>
/// services.AddQuartz(quartz => quartz
///     .AddIntervalJob&lt;UpdateAllShardMembersJob&gt;(TimeSpan.FromHours(6), startAfter: TimeSpan.FromMinutes(2))
///     .AddIntervalCommandJob&lt;RequestShardMemberRankSyncCommand&gt;(TimeSpan.FromMinutes(1)));
/// </code>
/// </example>
public static class IntervalJobSetup
{
    /// <summary>How long after the scheduler is built an interval job first runs, unless told otherwise.</summary>
    public static readonly TimeSpan DefaultStartAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs <typeparamref name="TJob"/> every <paramref name="interval"/>, first <paramref name="startAfter"/> (30 s by
    /// default) after the scheduler is built. Runs never overlap: a run that outlasts the interval holds the next one
    /// back until it ends (as <see cref="DisallowConcurrentExecutionAttribute"/> does; mark the job with it anyway, so
    /// it says so where it is written). The job's key is <see cref="JobKeyFor{TJob}"/>.
    /// </summary>
    /// <param name="quartz">The Quartz builder.</param>
    /// <param name="interval">Time between runs.</param>
    /// <param name="startAfter">Time from the scheduler's build to the first run; 30 s when <see langword="null"/>.</param>
    /// <param name="timeProvider">
    /// The clock the first run is computed from; the <see cref="TimeProvider"/> registered in DI when
    /// <see langword="null"/> (the system clock when none is). The scheduler itself runs on the registered one (Quartz 4).
    /// </param>
    public static IQuartzBuilder AddIntervalJob<TJob>(
        this IQuartzBuilder quartz,
        TimeSpan interval,
        TimeSpan? startAfter = null,
        TimeProvider? timeProvider = null)
        where TJob : class, IJob
    {
        ArgumentNullException.ThrowIfNull(quartz);
        return quartz.ScheduleIntervalJob<TJob>(
            JobKeyFor<TJob>(),
            interval,
            startAfter,
            timeProvider);
    }

    /// <summary>
    /// Sends a new <typeparamref name="TCommand"/> through the mediator every <paramref name="interval"/>, with
    /// <see cref="SendCommandJob{TCommand}"/>: no job class to write. Its key is
    /// <c>SendCommandJob-&lt;command type name&gt;</c>.
    /// </summary>
    /// <inheritdoc cref="AddIntervalJob{TJob}" path="/param"/>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TCommand"/> returns a value (<c>Result&lt;T&gt;</c>): use
    /// <see cref="AddIntervalCommandJob{TCommand, TResponse}(IQuartzBuilder, TimeSpan, TimeSpan?, TimeProvider?)"/>.
    /// </exception>
    public static IQuartzBuilder AddIntervalCommandJob<TCommand>(
        this IQuartzBuilder quartz,
        TimeSpan interval,
        TimeSpan? startAfter = null,
        TimeProvider? timeProvider = null)
        where TCommand : IRequest<Result>, new()
    {
        CommandJobs.EnsureReturns<TCommand, Result>();
        return quartz.AddIntervalJob<SendCommandJob<TCommand>>(
            interval,
            startAfter,
            timeProvider);
    }

    /// <summary>
    /// Sends a new <typeparamref name="TCommand"/> returning <typeparamref name="TResponse"/> (e.g. an
    /// <c>ICommand&lt;int&gt;</c>, whose result is <c>Result&lt;int&gt;</c>) through the mediator every
    /// <paramref name="interval"/>, with <see cref="SendCommandJob{TCommand, TResponse}"/>.
    /// </summary>
    /// <inheritdoc cref="AddIntervalJob{TJob}" path="/param"/>
    public static IQuartzBuilder AddIntervalCommandJob<TCommand, TResponse>(
        this IQuartzBuilder quartz,
        TimeSpan interval,
        TimeSpan? startAfter = null,
        TimeProvider? timeProvider = null)
        where TCommand : IRequest<TResponse>, new()
        where TResponse : Result
    {
        CommandJobs.EnsureReturns<TCommand, TResponse>();
        return quartz.AddIntervalJob<SendCommandJob<TCommand, TResponse>>(
            interval,
            startAfter,
            timeProvider);
    }

    /// <summary>
    /// Sends the <typeparamref name="TCommand"/> <paramref name="createCommand"/> makes through the mediator every
    /// <paramref name="interval"/>, for a command without a parameterless constructor (a record whose parameters are
    /// all optional) or one built from services. The job's key is <c>SendCommandJob-&lt;command type name&gt;</c>, the
    /// one <see cref="AddIntervalCommandJob{TCommand}(IQuartzBuilder, TimeSpan, TimeSpan?, TimeProvider?)"/> gives it,
    /// so moving between the two doesn't rename the job.
    /// </summary>
    /// <inheritdoc cref="AddIntervalJob{TJob}" path="/param"/>
    /// <param name="createCommand">
    /// Makes the command each run, from the run's DI scope (Quartz builds the job in a scope of its own per run), so it
    /// can read a <see cref="TimeProvider"/> or a scoped service.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TCommand"/> returns a value (<c>Result&lt;T&gt;</c>): use
    /// <see cref="AddIntervalCommandJob{TCommand, TResponse}(IQuartzBuilder, TimeSpan, Func{IServiceProvider, TCommand}, TimeSpan?, TimeProvider?)"/>.
    /// Or a factory for <typeparamref name="TCommand"/> is registered already.
    /// </exception>
    public static IQuartzBuilder AddIntervalCommandJob<TCommand>(
        this IQuartzBuilder quartz,
        TimeSpan interval,
        Func<IServiceProvider, TCommand> createCommand,
        TimeSpan? startAfter = null,
        TimeProvider? timeProvider = null)
        where TCommand : IRequest<Result>
    {
        CommandFactory.Register<TCommand, Result>(quartz, createCommand);
        return quartz.ScheduleIntervalJob<FactoryCommandJob<TCommand, Result>>(
            SendCommandJobKeyFor<TCommand>(),
            interval,
            startAfter,
            timeProvider);
    }

    /// <summary>
    /// Sends the <typeparamref name="TCommand"/> returning <typeparamref name="TResponse"/> that
    /// <paramref name="createCommand"/> makes through the mediator every <paramref name="interval"/>. The job's key is
    /// <c>SendCommandJob-&lt;command type name&gt;-&lt;response type name&gt;</c>, the one
    /// <see cref="AddIntervalCommandJob{TCommand, TResponse}(IQuartzBuilder, TimeSpan, TimeSpan?, TimeProvider?)"/>
    /// gives it.
    /// </summary>
    /// <inheritdoc cref="AddIntervalCommandJob{TCommand}(IQuartzBuilder, TimeSpan, Func{IServiceProvider, TCommand}, TimeSpan?, TimeProvider?)" path="/param"/>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TCommand"/> doesn't return <typeparamref name="TResponse"/>, or a factory for
    /// <typeparamref name="TCommand"/> is registered already.
    /// </exception>
    public static IQuartzBuilder AddIntervalCommandJob<TCommand, TResponse>(
        this IQuartzBuilder quartz,
        TimeSpan interval,
        Func<IServiceProvider, TCommand> createCommand,
        TimeSpan? startAfter = null,
        TimeProvider? timeProvider = null)
        where TCommand : IRequest<TResponse>
        where TResponse : Result
    {
        CommandFactory.Register<TCommand, TResponse>(quartz, createCommand);
        return quartz.ScheduleIntervalJob<FactoryCommandJob<TCommand, TResponse>>(
            SendCommandJobKeyFor<TCommand, TResponse>(),
            interval,
            startAfter,
            timeProvider);
    }

    /// <summary>
    /// The key <see cref="AddIntervalJob{TJob}"/> gives <typeparamref name="TJob"/>: its type name, with a generic job's
    /// type arguments after a dash (<c>SendCommandJob-RankSyncCommand</c>, as the outbox's
    /// <c>ProcessOutboxMessagesJob-ShardDbContext</c>).
    /// </summary>
    public static JobKey JobKeyFor<TJob>()
        where TJob : IJob
        => new(NameOf(typeof(TJob)));

    /// <summary>
    /// The key <see cref="SendCommandJob{TCommand}"/> gets, <c>SendCommandJob-&lt;command&gt;</c>; also for a command
    /// without a parameterless constructor, which that type can't be made with.
    /// </summary>
    internal static JobKey SendCommandJobKeyFor<TCommand>()
        => new(NameOf(typeof(SendCommandJob<>), typeof(TCommand)));

    /// <summary>
    /// The key <see cref="SendCommandJob{TCommand, TResponse}"/> gets,
    /// <c>SendCommandJob-&lt;command&gt;-&lt;response&gt;</c>; also for a command without a parameterless constructor.
    /// </summary>
    internal static JobKey SendCommandJobKeyFor<TCommand, TResponse>()
        => new(NameOf(typeof(SendCommandJob<,>), typeof(TCommand), typeof(TResponse)));

    // A job and its repeating trigger under the given key.
    private static IQuartzBuilder ScheduleIntervalJob<TJob>(
        this IQuartzBuilder quartz,
        JobKey jobKey,
        TimeSpan interval,
        TimeSpan? startAfter,
        TimeProvider? timeProvider)
        where TJob : class, IJob
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        if (startAfter < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(startAfter), startAfter, "The first run can't be in the past.");
        var delay = startAfter ?? DefaultStartAfter;

        return quartz
            .AddJob<TJob>(job => job
                .WithIdentity(jobKey)
                .DisallowConcurrentExecution())
            .AddTrigger<TJob>((provider, trigger) => trigger
                .ForJob(jobKey)
                .StartAt(Clock(provider, timeProvider).GetUtcNow() + delay)
                .WithSchedule(SimpleScheduleBuilder
                    .Create()
                    .WithInterval(interval)
                    .RepeatForever()));
    }

    private static string NameOf(Type type)
        => type.IsGenericType
            ? NameOf(type, type.GenericTypeArguments)
            : type.Name;

    // A generic type's name (or its definition's, for type arguments it can't be made with) and its type arguments'.
    private static string NameOf(
        Type genericType,
        params Type[] typeArguments)
    {
        var name = genericType.Name;
        var arity = name.IndexOf('`', StringComparison.Ordinal);
        if (arity >= 0)
            name = name[..arity];
        return string.Join('-', typeArguments.Select(NameOf).Prepend(name));
    }

    private static TimeProvider Clock(IServiceProvider provider, TimeProvider? timeProvider)
        => timeProvider
            ?? provider.GetService<TimeProvider>()
            ?? TimeProvider.System;
}
