using Microsoft.EntityFrameworkCore;
using Quartz;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Extensions;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>The outbox's Quartz job keys: one set per DbContext, so two contexts with an outbox don't collide.</summary>
public static class OutboxJobs
{
    /// <summary>The key of <typeparamref name="TDbContext"/>'s processing job: <c>ProcessOutboxMessagesJob-&lt;DbContext&gt;</c>.</summary>
    public static JobKey Process<TDbContext>()
        where TDbContext : DbContext
        => new($"ProcessOutboxMessagesJob-{typeof(TDbContext).Name}");

    /// <summary>The key of <typeparamref name="TDbContext"/>'s cleanup job: <c>CleanupOutboxMessagesJob-&lt;DbContext&gt;</c>.</summary>
    public static JobKey Cleanup<TDbContext>()
        where TDbContext : DbContext
        => new($"CleanupOutboxMessagesJob-{typeof(TDbContext).Name}");
}

/// <summary>The claim the regular outbox job of <typeparamref name="TDbContext"/> uses, if any.</summary>
internal sealed record OutboxJobClaim<TDbContext>(IOutboxLaneClaim? Claim)
    where TDbContext : DbContext
{
    /// <summary>The DbContext these settings belong to (the type parameter keys them in DI).</summary>
    public static System.Type DbContextType => typeof(TDbContext);
}

/// <summary>The lane options of <typeparamref name="TDbContext"/>: each DbContext with an outbox has its own.</summary>
internal sealed record OutboxLaneSettings<TDbContext>(OutboxLaneOptions Options)
    where TDbContext : DbContext
{
    /// <summary>The DbContext these settings belong to (the type parameter keys them in DI).</summary>
    public static System.Type DbContextType => typeof(TDbContext);
}
