using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.Abstractions.Health;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Health;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>Health checks for the outbox.</summary>
public static class OutboxHealthChecksBuilderExtensions
{
    /// <summary>The default for how long the oldest outbox message may wait before the check fails.</summary>
    public static readonly TimeSpan DefaultMaxWaitingAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Checks that <typeparamref name="TDbContext"/>'s outbox keeps up: it fails when its oldest waiting message has
    /// waited longer than <paramref name="maxWaitingAge"/> (5 minutes by default; keep it above the processing
    /// interval), or when the backlog can't be measured. It fails as <see cref="HealthStatus.Degraded"/> unless
    /// <paramref name="failureStatus"/> says otherwise: a backlog is a reason to alert, not to take every replica out of
    /// service at once (<c>/health/ready</c> answers 200 for Degraded).
    /// <para>
    /// Needs <typeparamref name="TDbContext"/>'s outbox processing, registered either way:
    /// <c>AddOutboxProcessing&lt;TDbContext&gt;</c> or <c>AddProcessOutboxMessagesJob&lt;TDbContext&gt;</c> on the Quartz
    /// builder (each registers the backlog monitor this check reads). Without either, the host fails to start (the check
    /// is validated at start-up, with a message naming the missing call); a container used without a host (no start-up
    /// validation) gets a check that reports the same message with <paramref name="failureStatus"/>, never an exception.
    /// </para>
    /// </summary>
    /// <param name="tags">The check's tags: <see cref="HealthTags.Ready"/> when <see langword="null"/>; <c>[]</c> for none.</param>
    /// <example><c>services.AddHealthChecks().AddOutbox&lt;ShardDbContext&gt;();</c></example>
    public static IHealthChecksBuilder AddOutbox<TDbContext>(
        this IHealthChecksBuilder builder,
        TimeSpan? maxWaitingAge = null,
        string? name = null,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        var age = maxWaitingAge ?? DefaultMaxWaitingAge;
        if (age <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxWaitingAge), maxWaitingAge, "The waiting age must be positive.");

        var missingProcessing = MissingProcessing<TDbContext>();
        // Fails the host's start (before any hosted service starts) instead of every check run.
        builder.Services
            .AddOptions<OutboxHealthCheckRequirement<TDbContext>>()
            .Validate<IServiceProviderIsService>(
                (_, services) => services.IsService(typeof(OutboxBacklogMonitor<TDbContext>)),
                missingProcessing)
            .ValidateOnStart();

        return builder.Add(new HealthCheckRegistration(
            name ?? $"outbox-{typeof(TDbContext).Name}",
            provider => Create<TDbContext>(provider, age, missingProcessing),
            failureStatus ?? HealthStatus.Degraded,
            tags ?? [HealthTags.Ready]));
    }

    private static IHealthCheck Create<TDbContext>(
        IServiceProvider provider,
        TimeSpan maxWaitingAge,
        string missingProcessing)
        where TDbContext : DbContext
    {
        if (provider.GetService<OutboxBacklogMonitor<TDbContext>>() is not { } monitor)
            return new MisconfiguredHealthCheck(missingProcessing);

        return new OutboxHealthCheck<TDbContext>(
            monitor,
            maxWaitingAge,
            provider.GetRequiredService<TimeProvider>());
    }

    private static string MissingProcessing<TDbContext>()
        where TDbContext : DbContext
    {
        var context = typeof(TDbContext).Name;
        return $"The outbox health check for {context} reads the outbox backlog monitor, which no outbox processing "
            + $"registered: call services.AddOutboxProcessing<{context}>(...) or, on the Quartz builder, "
            + $"quartz.AddProcessOutboxMessagesJob<{context}>(...).";
    }
}

/// <summary>Marks that an outbox health check for <typeparamref name="TDbContext"/> is registered, for start-up validation.</summary>
internal sealed class OutboxHealthCheckRequirement<TDbContext>
    where TDbContext : DbContext
{
    /// <summary>The DbContext the check belongs to (the type parameter keys it in DI).</summary>
    public Type DbContextType { get; } = typeof(TDbContext);
}
