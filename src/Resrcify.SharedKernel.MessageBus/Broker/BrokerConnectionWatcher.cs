using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.Broker;

/// <summary>
/// Watches the connection to RabbitMQ and raises <see cref="Recovered"/> when it is back after being lost (or
/// reachable for the first time after start-up failed to reach it).
/// </summary>
/// <remarks>
/// <para>
/// Rebus re-subscribes a queue's consumer after its connection drops, but if the broker is still unreachable at
/// that moment (a restart takes a few seconds) it waits a fixed minute before trying again, so the queue is not
/// consumed for that minute. The buses this package runs itself (the scatter-gather reply bus, each rate-limited
/// queue) restart when the broker is back, which subscribes again at once; the service's own bus is Rebus' and resumes
/// within that minute, with no message lost.
/// </para>
/// <para>
/// It connects as the buses do: the service's <c>RabbitMqConnection</c> (its virtual host and TLS), then the
/// configuration strategy's <c>ConfigureConnectionFactory</c>. Any failure to connect (unreachable, refused, a
/// virtual host the user may not use) is tried again every second rather than stopping the service.
/// </para>
/// </remarks>
internal sealed partial class BrokerConnectionWatcher(
    MessageBusSettings settings,
    IServiceProvider serviceProvider,
    ILogger<BrokerConnectionWatcher> logger)
    : BackgroundService
{
    private static TimeSpan RetryInterval => TimeSpan.FromSeconds(1);

    private IConnection? _connection;

    /// <summary>The broker is reachable again: buses that consume should restart.</summary>
    public event Action? Recovered;

    /// <summary>Whether the connection to RabbitMQ is open now (false before the first connect and while recovering).</summary>
    public bool IsConnected => _connection?.IsOpen == true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var time = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        var factory = settings.CreateConnectionFactory(serviceProvider, "messagebus-broker-watcher");
        factory.AutomaticRecoveryEnabled = true;
        factory.NetworkRecoveryInterval = RetryInterval;
        factory.TopologyRecoveryEnabled = false;

        var failedFirst = false;
        while (_connection is null)
        {
            try
            {
                _connection = await factory.CreateConnectionAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested && exception is not OutOfMemoryException)
            {
                if (!failedFirst)
                    LogCantConnect(exception);
                failedFirst = true;
                if (!await DelayAsync(time, stoppingToken))
                    return;
            }
        }

        _connection.ConnectionShutdownAsync += (_, args) =>
        {
            if (args.Initiator != ShutdownInitiator.Application)
                LogLost(args.ReplyText);
            return Task.CompletedTask;
        };
        _connection.RecoverySucceededAsync += (_, _) =>
        {
            RaiseRecovered();
            return Task.CompletedTask;
        };
        if (failedFirst)
            RaiseRecovered();
    }

    public override void Dispose()
    {
        _connection?.Dispose();
        base.Dispose();
    }

    private void RaiseRecovered()
    {
        LogRecovered();
        Recovered?.Invoke();
    }

    private static async Task<bool> DelayAsync(TimeProvider time, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(RetryInterval, time, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't connect to RabbitMQ; trying again every second")]
    private partial void LogCantConnect(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost the connection to RabbitMQ: {Reason}")]
    private partial void LogLost(string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "RabbitMQ is reachable again: restarting the message bus' consumers")]
    private partial void LogRecovered();
}
