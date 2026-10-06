using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Outbox;

/// <summary>How <typeparamref name="TContext"/>'s outbox wake-up listens.</summary>
internal sealed record OutboxWakeUpSettings<TContext>(OutboxWakeUpOptions Options)
    where TContext : DbContext
{
    /// <summary>The DbContext these settings belong to (the type parameter keys them in DI).</summary>
    public static Type DbContextType => typeof(TContext);
}

/// <summary>
/// Listens for <c>NOTIFY resrcify_outbox</c> on one dedicated connection and wakes <typeparamref name="TContext"/>'s
/// outbox (<see cref="OutboxWakeUp{TDbContext}"/>) when a notification names it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Cheap: one idle connection; notifications arriving within <see cref="OutboxWakeUpOptions.Debounce"/> of each
/// other wake the outbox once, and the wake-up itself coalesces with a run already on its way.</item>
/// <item>Robust: a lost connection (a restart, a failover, a dead socket noticed by the keepalive) is logged and
/// replaced, waiting 1 s doubling to 30 s between tries, on the <see cref="TimeProvider"/>. Each (re)connect wakes the
/// outbox once, for the messages saved while nobody listened.</item>
/// <item>Shutdown cancels the wait and closes the connection.</item>
/// </list>
/// </remarks>
internal sealed partial class PostgresOutboxListener<TContext>(
    PostgresConnection<TContext> connection,
    OutboxWakeUpSettings<TContext> settings,
    OutboxWakeUp<TContext> wakeUp,
    ILogger<PostgresOutboxListener<TContext>> logger,
    TimeProvider? timeProvider = null)
    : BackgroundService
    where TContext : DbContext
{
    private readonly OutboxWakeUpOptions _options = settings.Options;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly string _context = typeof(TContext).Name;
    private TimeSpan _reconnectDelay = settings.Options.ReconnectDelay;
    private int _notified;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogConnectionLost(exception, _context, _reconnectDelay.TotalSeconds);
            }

            if (!await WaitToReconnectAsync(stoppingToken))
                return;
        }
    }

    /// <summary>Connects, listens, and wakes the outbox on every notification for this context, until it fails.</summary>
    private async Task ListenAsync(CancellationToken stoppingToken)
    {
        await using var listener = new NpgsqlConnection(ListenerConnectionString());
        listener.Notification += OnNotification;
        await listener.OpenAsync(stoppingToken);
        await using (var listen = new NpgsqlCommand($"LISTEN {OutboxWakeUpOptions.Channel}", listener))
            await listen.ExecuteNonQueryAsync(stoppingToken);

        _reconnectDelay = _options.ReconnectDelay;
        LogListening(_context, OutboxWakeUpOptions.Channel);
        // Messages saved while nobody listened (before start, or while reconnecting) don't wait for the next poll.
        await WakeAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await listener.WaitAsync(stoppingToken);
            if (Interlocked.Exchange(ref _notified, 0) == 0)
                continue;

            if (_options.Debounce > TimeSpan.Zero)
                await Task.Delay(_options.Debounce, _time, stoppingToken);
            await WakeAsync(stoppingToken);
        }
    }

    private void OnNotification(
        object sender,
        NpgsqlNotificationEventArgs notification)
    {
        if (string.Equals(notification.Payload, PostgresOutboxNotifier<TContext>.Payload, StringComparison.Ordinal))
            Volatile.Write(ref _notified, 1);
    }

    private async Task WakeAsync(CancellationToken stoppingToken)
    {
        LogWaking(_context);
        await wakeUp.WakeAsync(stoppingToken);
    }

    /// <summary>Waits before the next connect, doubling the wait up to the maximum; false when shutting down.</summary>
    private async Task<bool> WaitToReconnectAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_reconnectDelay, _time, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }

        var doubled = _reconnectDelay * 2;
        _reconnectDelay = doubled < _options.MaxReconnectDelay ? doubled : _options.MaxReconnectDelay;
        return true;
    }

    /// <summary>The context's connection string, unpooled (the connection lives as long as the listener), with a keepalive.</summary>
    private string ListenerConnectionString()
        => new NpgsqlConnectionStringBuilder(connection.ConnectionString)
        {
            Pooling = false,
            KeepAlive = _options.KeepAliveInSeconds,
            ApplicationName = $"{_context} outbox listener",
        }.ConnectionString;

    [LoggerMessage(Level = LogLevel.Information, Message = "The {DbContext} outbox listens for new messages on {Channel}")]
    private partial void LogListening(string dbContext, string channel);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Waking the {DbContext} outbox")]
    private partial void LogWaking(string dbContext);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {DbContext} outbox listener lost its connection; it reconnects in {DelaySeconds} s and the polling covers the gap")]
    private partial void LogConnectionLost(Exception exception, string dbContext, double delaySeconds);
}
