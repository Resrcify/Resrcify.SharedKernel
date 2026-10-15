using System;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Extensions;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>The outbox's Quartz job keys: one set per DbContext, so two contexts with an outbox don't collide.</summary>
public static class OutboxJobs
{
    private const string ProcessPrefix = "ProcessOutboxMessagesJob-";
    private const string CleanupPrefix = "CleanupOutboxMessagesJob-";

    /// <summary>The key of <typeparamref name="TDbContext"/>'s processing job: <c>ProcessOutboxMessagesJob-&lt;DbContext&gt;</c>.</summary>
    public static JobKey Process<TDbContext>()
        where TDbContext : DbContext
        => new(ProcessPrefix + typeof(TDbContext).Name);

    /// <summary>The key of <typeparamref name="TDbContext"/>'s cleanup job: <c>CleanupOutboxMessagesJob-&lt;DbContext&gt;</c>.</summary>
    public static JobKey Cleanup<TDbContext>()
        where TDbContext : DbContext
        => new(CleanupPrefix + typeof(TDbContext).Name);

    /// <summary>
    /// Whether <paramref name="job"/> is one of an outbox's jobs (processing or cleanup, of any DbContext): a key
    /// <see cref="Process{TDbContext}"/> or <see cref="Cleanup{TDbContext}"/> gives. The <c>AddQuartzJobs</c> liveness
    /// check leaves them out unless told otherwise.
    /// </summary>
    public static bool IsOutboxJob(JobKey job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Group == JobKey.DefaultGroup
            && (job.Name.StartsWith(ProcessPrefix, StringComparison.Ordinal)
                || job.Name.StartsWith(CleanupPrefix, StringComparison.Ordinal));
    }
}

/// <summary>The claim the regular outbox job of <typeparamref name="TDbContext"/> uses, if any.</summary>
internal sealed record OutboxJobClaim<TDbContext>(IOutboxLaneClaim? Claim)
    where TDbContext : DbContext
{
    /// <summary>The DbContext these settings belong to (the type parameter keys them in DI).</summary>
    public static System.Type DbContextType => typeof(TDbContext);
}

/// <summary>
/// The claim <typeparamref name="TDbContext"/>'s outbox job and lanes use when the service set none: registered by
/// the package that writes the outbox (the Postgres package's <c>WithOutbox()</c> registers its
/// <see cref="PostgresOutboxLaneClaim"/>), whatever the order of the registrations.
/// </summary>
internal sealed record OutboxDefaultClaim<TDbContext>(IOutboxLaneClaim Claim)
    where TDbContext : DbContext
{
    /// <summary>The claim the job and the lanes use: the one set, else the default one, else none.</summary>
    public static IOutboxLaneClaim? Effective(
        IOutboxLaneClaim? set,
        System.IServiceProvider services)
        => set ?? (services.GetService(typeof(OutboxDefaultClaim<TDbContext>)) as OutboxDefaultClaim<TDbContext>)?.Claim;
}

/// <summary>The lane options of <typeparamref name="TDbContext"/>: each DbContext with an outbox has its own.</summary>
/// <param name="Options">The options, as configured.</param>
/// <param name="RetryLimits">The retry limits the options make (<see cref="OutboxRetryLimits.Create"/>), made once.</param>
internal sealed record OutboxLaneSettings<TDbContext>(OutboxLaneOptions Options, OutboxRetryLimits RetryLimits)
    where TDbContext : DbContext
{
    /// <summary>The DbContext these settings belong to (the type parameter keys them in DI).</summary>
    public static System.Type DbContextType => typeof(TDbContext);
}
