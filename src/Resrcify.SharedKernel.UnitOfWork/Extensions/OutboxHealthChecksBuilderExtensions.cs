using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
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
    /// interval), or when the backlog can't be measured. Needs <c>AddOutboxProcessing&lt;TDbContext&gt;</c>.
    /// </summary>
    /// <example><c>services.AddHealthChecks().AddOutbox&lt;ShardDbContext&gt;(tags: ["ready"]);</c></example>
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
        return builder.Add(new HealthCheckRegistration(
            name ?? $"outbox-{typeof(TDbContext).Name}",
            provider => new OutboxHealthCheck<TDbContext>(
                provider.GetService<OutboxBacklogMonitor<TDbContext>>()
                    ?? throw new InvalidOperationException(
                        $"The outbox health check needs AddOutboxProcessing<{typeof(TDbContext).Name}>() first."),
                age,
                provider.GetService<TimeProvider>() ?? TimeProvider.System),
            failureStatus,
            tags));
    }
}
