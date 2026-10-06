using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.MessageBus.Diagnostics;

namespace Resrcify.SharedKernel.MessageBus.Extensions;

/// <summary>Registers the message bus' health check.</summary>
public static class MessageBusHealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds a check that is unhealthy (or <paramref name="failureStatus"/>) while RabbitMQ is unreachable, and lists
    /// each rate-limited queue as consuming or stepped aside. Always healthy on the in-memory transport.
    /// </summary>
    /// <remarks>
    /// Give it your readiness tag if a pod without a broker should take no traffic. Never give it the tag a
    /// rate-limited queue is gated on (<c>RateLimitedQueueOptions.HealthCheckTag</c>): the queue would stop whenever
    /// the broker blips, which it already recovers from by itself.
    /// </remarks>
    /// <example><c>services.AddHealthChecks().AddMessageBus(tags: ["ready"]);</c></example>
    public static IHealthChecksBuilder AddMessageBus(
        this IHealthChecksBuilder builder,
        string name = "messagebus",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddCheck<MessageBusHealthCheck>(name, failureStatus, tags ?? []);
    }
}
