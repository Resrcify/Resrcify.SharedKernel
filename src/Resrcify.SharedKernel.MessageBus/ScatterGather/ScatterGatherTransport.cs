using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Activation;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Messages;
using Rebus.Retry;
using Rebus.Routing.TypeBased;
using Rebus.Transport;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Broker;
using Resrcify.SharedKernel.MessageBus.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>
/// Sends scatter requests from this instance and collects their replies in memory. Replies come back
/// on a queue private to this instance (a unique name, non-durable, deleted by RabbitMQ when the instance
/// disconnects), so with several instances each one receives only the replies it is waiting for.
/// Not exclusive: RabbitMQ ties an exclusive queue to one connection, and Rebus uses more than one.
/// Also the service's <see cref="IScatterGatherClient"/>.
/// </summary>
internal sealed partial class ScatterGatherTransport(
    MessageBusSettings settings,
    TimeProvider time,
    IServiceProvider serviceProvider,
    ILogger<ScatterGatherTransport> logger)
    : IScatterGatherClient, IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<Guid, PendingBatch> _pending = new();
    private readonly Lock _gate = new();
    private Action? _unsubscribeFromBroker;
    private string? _queue;
    private BuiltinHandlerActivator? _activator;
    private IBus? _bus;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // One queue name for the instance's lifetime: a restarted bus keeps receiving the replies to requests
        // sent before it restarted.
        _queue = string.Create(
            CultureInfo.InvariantCulture,
            $"{settings.InputQueue ?? "scatter"}.replies.{Environment.MachineName}-{Guid.NewGuid():N}");
        lock (_gate)
            StartBus();
        if (serviceProvider.GetService<BrokerConnectionWatcher>() is { } watcher)
        {
            watcher.Recovered += OnBrokerRecovered;
            _unsubscribeFromBroker = () => watcher.Recovered -= OnBrokerRecovered;
        }
        LogStarted(_queue);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _unsubscribeFromBroker?.Invoke();
        _unsubscribeFromBroker = null;
        lock (_gate)
            StopBus();
    }

    /// <summary>The broker is back: restart the bus, so its consumer subscribes at once (see <see cref="BrokerConnectionWatcher"/>).</summary>
    private void OnBrokerRecovered()
        => _ = Task.Run(() =>
        {
            lock (_gate)
            {
                if (_activator is null)
                    return;   // stopped
                StopBus();
                StartBus();
            }
            LogRestarted(_queue!);
        });

    private void StartBus()
    {
        var strategy = settings.ResolveConfigurationStrategy(serviceProvider);
        _activator = new BuiltinHandlerActivator();
        _activator.Handle<object>((_, context, message) =>
        {
            Record(context.Headers, message);
            return Task.CompletedTask;
        });
        _bus = Configure.With(_activator)
            .Logging(logging => logging.Use(new RebusLoggerFactory(serviceProvider.GetRequiredService<ILoggerFactory>())))
            .Transport(transport => settings.ConfigureTransport(transport, serviceProvider, _queue, strategy, rabbitMq => rabbitMq
                .InputQueueOptions(options => options.SetDurable(false).SetAutoDelete(true))
                .Prefetch(settings.Prefetch)))
            .Serialization(serializer => settings.ConfigureSerialization(serializer, serviceProvider))
            .Routing(routing =>
            {
                var typeBased = routing.TypeBased();
                foreach (var (messageType, destination) in settings.Destinations)
                    typeBased.Map(messageType, destination);
            })
            .Options(options =>
            {
                options.SetNumberOfWorkers(1);
                options.SetMaxParallelism(settings.MaxParallelism);
                options.Decorate<ITransport>(context =>
                {
                    var transport = context.Get<ITransport>();
                    foreach (var destination in settings.Destinations.Values)
                        transport.CreateQueue(destination);
                    return transport;
                });
                settings.ConfigureEveryBus(options);
                options.Register<IErrorHandler>(_ => new DropUnreadableReplyErrorHandler(logger));
                strategy.ConfigureOptions(options);
            })
            .Start();
    }

    private void StopBus()
    {
        _bus?.Dispose();
        _bus = null;
        _activator?.Dispose();
        _activator = null;
    }

    public async Task<Result<TResponse>> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(request);
        const string key = "request";
        var gathered = await GatherAsync<TRequest, TResponse>(
            new Dictionary<string, TRequest>(StringComparer.Ordinal) { [key] = request },
            timeout,
            cancellationToken);
        return gathered[key];
    }

    public async Task<IGathered<TResponse>> GatherAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(requests);
        var (batchId, pending) = Register(requests.Keys, retainReplies: true);
        var started = time.GetTimestamp();
        try
        {
            await SendAsync(batchId, requests, timeout);

            // The timeout runs on the TimeProvider's clock (a fake clock ends a gather too).
            using var timedOut = new CancellationTokenSource(timeout, time);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timedOut.Token);
            try
            {
                await pending.AllAnswered.WaitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timed out: gather with whatever has arrived.
            }
            var gathered = pending.ToGathered<TResponse>();
            MessageBusDiagnostics.RecordBatch(
                settings.WireNameOf(typeof(TRequest)),
                BatchItems.Of(gathered),
                gathered.IsComplete ? BatchEnd.Complete : BatchEnd.Timeout,
                time.GetElapsedTime(started));
            return gathered;
        }
        finally
        {
            _pending.TryRemove(batchId, out _);
        }
    }

    public IAsyncEnumerable<IScatterReply<TResponse>> StreamAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(requests);
        return StreamRepliesAsync<TRequest, TResponse>(requests, timeout, cancellationToken);
    }

    private async IAsyncEnumerable<IScatterReply<TResponse>> StreamRepliesAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        // A streamed batch keeps only which items have answered: a reply is the caller's once read, so the
        // batch holds what the caller hasn't read yet, not every reply.
        var (batchId, pending) = Register(requests.Keys, retainReplies: false);
        var started = time.GetTimestamp();
        var items = new BatchItems();
        var end = BatchEnd.Stopped;
        try
        {
            await SendAsync(batchId, requests, timeout);

            // The timeout runs on the TimeProvider's clock (a fake clock ends a gather too).
            using var timedOut = new CancellationTokenSource(timeout, time);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timedOut.Token);
            while (await NextArrivalAsync(pending, timeoutSource.Token, cancellationToken) is { } arrival)
            {
                var reply = PendingBatch.ToReply<TResponse>(arrival);
                items = items.Add(reply.Result);
                yield return reply;
            }
            end = items.Replied == requests.Count ? BatchEnd.Complete : BatchEnd.Timeout;
        }
        finally
        {
            // Replies after this (the caller stopped, or the timeout passed) are late and ignored.
            _pending.TryRemove(batchId, out _);
            MessageBusDiagnostics.RecordBatch(
                settings.WireNameOf(typeof(TRequest)),
                items with { Unanswered = requests.Count - items.Replied },
                end,
                time.GetElapsedTime(started));
        }
    }

    private (Guid BatchId, PendingBatch Pending) Register(IEnumerable<string> keys, bool retainReplies)
    {
        var batchId = Guid.NewGuid();
        var pending = new PendingBatch(keys, retainReplies);
        _pending[batchId] = pending;
        return (batchId, pending);
    }

    private async Task SendAsync<TRequest>(Guid batchId, IReadOnlyDictionary<string, TRequest> requests, TimeSpan timeout)
        where TRequest : class
    {
        var bus = _bus ?? throw new InvalidOperationException("The scatter-gather transport has not started.");
        var expiresAfter = timeout.ToString("c", CultureInfo.InvariantCulture);
        var batch = batchId.ToString("D");
        // One Rebus transaction for the whole batch: the sends are collected and published together when it
        // completes, instead of one broker round trip each (sending one by one capped a batch at ~170/s).
        using var sending = new RebusTransactionScope();
        foreach (var (key, request) in requests)
            await bus.Send(request, new Dictionary<string, string>
            {
                [ScatterHeaders.BatchId] = batch,
                [ScatterHeaders.ItemKey] = key,
                [Headers.TimeToBeReceived] = expiresAfter,
            });
        await sending.CompleteAsync();
    }

    /// <summary>The next reply, or <see langword="null"/> once every item has answered or the timeout has passed.</summary>
    private static async Task<KeyValuePair<string, object>?> NextArrivalAsync(
        PendingBatch pending,
        CancellationToken timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await pending.Arrivals.WaitToReadAsync(timeout))
                if (pending.Arrivals.TryRead(out var arrival))
                    return arrival;
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;   // timed out
        }
    }

    private void Record(IReadOnlyDictionary<string, string> headers, object message)
    {
        if (!ScatterHeaders.TryRead(headers, out var batchId, out var itemKey))
            return;   // not a scatter reply
        var outcome = _pending.TryGetValue(batchId, out var pending)
            ? pending.Record(itemKey, message)
            : ReplyOutcome.Late;   // its batch has already been gathered
        MessageBusDiagnostics.RecordReply(outcome);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scatter-gather replies arrive on {Queue}")]
    private partial void LogStarted(string queue);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restarted the scatter-gather reply bus on {Queue} after RabbitMQ came back")]
    private partial void LogRestarted(string queue);
}
