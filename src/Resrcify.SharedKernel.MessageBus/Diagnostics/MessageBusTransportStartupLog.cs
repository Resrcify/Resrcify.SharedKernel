using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Resrcify.SharedKernel.MessageBus.ScatterGather;

namespace Resrcify.SharedKernel.MessageBus.Diagnostics;

/// <summary>
/// Says at start which transport <c>UseTransportFromConfiguration</c> chose: RabbitMQ and its broker (never its
/// password), or in memory. In memory outside Development is a warning, so an instance deployed without its
/// <c>MessageBus</c> section (Production refuses to start without it) is visible in its log. The in-memory line says
/// what that costs: for a bus that only publishes, that nothing it publishes leaves the process; for one that receives
/// (an input queue, a rate-limited queue, scatter-gather's replies), that nothing outside the process reaches it either.
/// Silent once the bus was given another transport afterwards: it no longer runs on the one chosen.
/// </summary>
internal sealed partial class MessageBusTransportStartupLog(
    MessageBusSettings settings,
    IServiceProvider serviceProvider,
    ILogger<MessageBusTransportStartupLog> logger)
    : IHostedService
{
    private readonly ILogger<MessageBusTransportStartupLog> _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var selection = settings.TransportSelection;
        if (selection is null || !selection.IsInUseBy(settings))
            return Task.CompletedTask;

        if (selection.IsRabbitMq)
            LogTheBroker();
        else
            LogInMemory(selection.Environment);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private void LogTheBroker()
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<RabbitMqOptions>>()
            .Value;
        LogRabbitMq(
            options.Host,
            options.Port,
            options.VirtualHost);
    }

    private void LogInMemory(IHostEnvironment environment)
    {
        var receives = Receives();
        var inDevelopment = environment.IsDevelopment();
        if (inDevelopment && receives)
            LogReceivingInMemory();
        else if (inDevelopment)
            LogPublishingInMemory();
        else if (receives)
            LogReceivingInMemoryOutsideDevelopment(environment.EnvironmentName);
        else
            LogPublishingInMemoryOutsideDevelopment(environment.EnvironmentName);
    }

    /// <summary>Whether anything outside the process would send to this service: its input queue, a rate-limited queue, scatter-gather's replies.</summary>
    private bool Receives()
        => settings.InputQueue is not null
            || serviceProvider.GetServices<IQueueConsumer>().Any()
            || serviceProvider.GetService<ScatterGatherTransport>() is not null;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Message bus: RabbitMQ at {Host}:{Port}, virtual host {VirtualHost} (the MessageBus section)")]
    private partial void LogRabbitMq(
        string host,
        int port,
        string virtualHost);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Message bus: in memory (no MessageBus section): nothing it publishes leaves this process")]
    private partial void LogPublishingInMemory();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Message bus: in memory (no MessageBus section): nothing outside this process reaches its queues, and nothing it sends or publishes leaves it")]
    private partial void LogReceivingInMemory();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Message bus: in memory (no MessageBus section) in {Environment}: nothing it publishes leaves this process. Set MessageBus__Host, MessageBus__Username and MessageBus__Password to publish on RabbitMQ")]
    private partial void LogPublishingInMemoryOutsideDevelopment(string environment);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Message bus: in memory (no MessageBus section) in {Environment}: nothing outside this process reaches its queues, and nothing it sends or publishes leaves it. Set MessageBus__Host, MessageBus__Username and MessageBus__Password to run it on RabbitMQ")]
    private partial void LogReceivingInMemoryOutsideDevelopment(string environment);
}
