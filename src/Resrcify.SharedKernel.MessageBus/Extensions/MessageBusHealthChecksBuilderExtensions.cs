using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.Abstractions.Health;
using Resrcify.SharedKernel.MessageBus.Diagnostics;

namespace Resrcify.SharedKernel.MessageBus.Extensions;

/// <summary>Registers the message bus' health check.</summary>
public static class MessageBusHealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds a check that is unhealthy (or <paramref name="failureStatus"/>) while RabbitMQ is unreachable, and lists
    /// each rate-limited queue as consuming or stepped aside. Always healthy on the in-memory transport. It watches the
    /// bus' own connection, so it replaces a RabbitMQ check of the service's (<c>AddRabbitMQ</c>, and the
    /// <c>IConnection</c> built by hand for it).
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The check's name.</param>
    /// <param name="failureStatus">The status while the broker is unreachable (<see cref="HealthStatus.Unhealthy"/>).</param>
    /// <param name="tags">The check's tags: <see cref="HealthTags.Ready"/> when <see langword="null"/>; <c>[]</c> for none.</param>
    /// <remarks>
    /// A pod without a broker takes no traffic (it is a readiness check by default). Never give it the tag a
    /// rate-limited queue is gated on (<c>RateLimitedQueueOptions.HealthCheckTag</c>): the queue would stop whenever
    /// the broker blips, which it already recovers from by itself.
    /// </remarks>
    /// <example><c>services.AddHealthChecks().AddMessageBus();</c></example>
    public static IHealthChecksBuilder AddMessageBus(
        this IHealthChecksBuilder builder,
        string name = "messagebus",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddCheck<MessageBusHealthCheck>(name, failureStatus, tags ?? [HealthTags.Ready]);
    }
}
