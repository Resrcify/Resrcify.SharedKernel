using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.MessageBus.Broker;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Resrcify.SharedKernel.MessageBus.ScatterGather;

namespace Resrcify.SharedKernel.MessageBus.Diagnostics;

/// <summary>
/// The message bus' health: unhealthy (or the registration's failure status) while RabbitMQ is unreachable.
/// The data shows each rate-limited queue as <c>consuming</c> or <c>stepped aside</c>; a queue that stepped aside
/// doesn't change the status, since its own health check (e.g. the upstream's) already reports why.
/// </summary>
/// <remarks>
/// Don't give it the tag a rate-limited queue is gated on: the queue would then stop whenever the broker blips,
/// which it already recovers from by itself.
/// </remarks>
internal sealed class MessageBusHealthCheck(
    MessageBusSettings settings,
    IServiceProvider serviceProvider)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var queue in serviceProvider.GetServices<IQueueConsumer>())
            data[queue.QueueName] = queue.IsConsuming ? "consuming" : "stepped aside";

        if (settings.IsInMemory)
        {
            data["transport"] = "in-memory";
            return Task.FromResult(HealthCheckResult.Healthy("In memory.", data));
        }

        data["transport"] = "rabbitmq";
        var watcher = serviceProvider.GetService<BrokerConnectionWatcher>();
        var connected = watcher?.IsConnected == true;
        data["broker"] = connected ? "connected" : "unreachable";
        if (!connected)
            return Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus, "RabbitMQ is unreachable.", data: data));

        // Started but without a bus: a restart after the broker came back failed, and is being tried again.
        if (serviceProvider.GetService<ScatterGatherTransport>() is { IsStarted: true } scatterGather)
        {
            data["scatter_gather"] = scatterGather.IsRunning ? "running" : "restarting";
            if (!scatterGather.IsRunning)
                return Task.FromResult(new HealthCheckResult(
                    context.Registration.FailureStatus,
                    "The scatter-gather reply bus isn't running: its restart after RabbitMQ came back failed, and is tried again.",
                    data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Connected to RabbitMQ.", data));
    }
}
