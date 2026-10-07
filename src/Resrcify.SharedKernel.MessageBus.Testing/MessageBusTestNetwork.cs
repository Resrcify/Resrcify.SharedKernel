using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rebus.Messages;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.Testing;

/// <summary>
/// The in-memory network the services of a test share, as they would share RabbitMQ: pass the same network to each
/// one's <c>AddMessageBusTestHarness</c>. It knows when every bus on it is done (<see cref="WaitUntilIdleAsync"/>).
/// </summary>
public sealed class MessageBusTestNetwork
{
    /// <summary>How long <see cref="WaitUntilIdleAsync"/> waits unless told otherwise.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(10);

    // Idle must hold for this many checks in a row with nothing received meanwhile: a message taken off its queue
    // is counted as being handled a moment after it left the queue.
    private const int QuietChecks = 3;

    private long _beingHandled;
    private long _received;

    /// <summary>The Rebus network underneath.</summary>
    public InMemNetwork Network { get; } = new();

    /// <summary>
    /// Waits until no bus on the network is handling a message and no queue holds one to deliver: everything sent so
    /// far, and everything its handlers sent in turn, has been handled. Error queues and messages deferred to later
    /// aren't waited for (advance the service's clock for those). Throws a <see cref="TimeoutException"/> saying what is
    /// still busy when that doesn't happen within <paramref name="timeout"/> (real time).
    /// </summary>
    /// <remarks>
    /// Only the buses: an outbox the handlers write to is drained with <c>OutboxWakeUp&lt;TDbContext&gt;.DrainAsync</c>,
    /// whose handlers may publish again; alternate the two until both are done.
    /// </remarks>
    public async Task WaitUntilIdleAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? DefaultTimeout;
        var started = TimeProvider.System.GetTimestamp();
        var quiet = 0;
        var lastReceived = -1L;
        while (true)
        {
            var received = Interlocked.Read(ref _received);
            quiet = IsIdle() && received == lastReceived ? quiet + 1 : 0;
            lastReceived = received;
            if (quiet >= QuietChecks)
                return;
            if (TimeProvider.System.GetElapsedTime(started) > limit)
                throw new TimeoutException($"The message buses were still busy after {limit.TotalSeconds} s: {Describe()}.");
            await Task.Delay(CheckEvery, cancellationToken);
        }
    }

    /// <summary>
    /// Starts a stand-in for another service's rate-limited queue on this network: requests for
    /// <typeparamref name="TRequest"/> sent to <paramref name="queue"/> are answered by <paramref name="respond"/>, through
    /// the same queue and reply path the real service would use. Dispose it to stop it.
    /// </summary>
    /// <param name="queue">The queue the requesters send to; <see langword="null"/>: the request's default queue.</param>
    /// <param name="configure">
    /// The stand-in's own bus settings, e.g. the wire names the real service gives the messages
    /// (<c>AddMessage&lt;T&gt;(wireName)</c>).
    /// </param>
    public async Task<MessageBusStandIn> StartResponderAsync<TRequest, TResponse>(
        Func<TRequest, CancellationToken, Task<Result<TResponse>>> respond,
        string? queue = null,
        Action<MessageBusBuilder>? configure = null)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(respond);
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(new StandInResponse<TRequest, TResponse>(respond));
        builder.Services.AddMessageBus(bus =>
        {
            bus.UseInMemory(Network);
            configure?.Invoke(bus);
            bus.AddRateLimitedQueue<TRequest, TResponse, StandInResponder<TRequest, TResponse>>(queue);
        });
        builder.Services.AddMessageBusTestHarness(this);
        var host = builder.Build();
        try
        {
            await host.StartAsync();
        }
        catch
        {
            host.Dispose();
            throw;
        }

        return new MessageBusStandIn(host);
    }

    internal void Received()
    {
        Interlocked.Increment(ref _beingHandled);
        Interlocked.Increment(ref _received);
    }

    internal void Handled()
        => Interlocked.Decrement(ref _beingHandled);

    private bool IsIdle()
        => Interlocked.Read(ref _beingHandled) == 0 && WaitingMessages().Count == 0;

    // Per queue, the messages waiting to be delivered now (not in an error queue, not deferred).
    private Dictionary<string, int> WaitingMessages()
    {
        var waiting = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var queue in Network.Queues.Where(queue => !IsErrorQueue(queue)))
        {
            var count = Network.GetMessages(queue).Count(message => !message.Headers.ContainsKey(Headers.DeferredUntil));
            if (count > 0)
                waiting[queue] = count;
        }

        return waiting;
    }

    private static bool IsErrorQueue(string queue)
        => queue == "error" || queue.EndsWith(".error", StringComparison.Ordinal);

    private string Describe()
    {
        var waiting = WaitingMessages();
        var queues = waiting.Count == 0
            ? "no queue holds a message"
            : string.Join(", ", waiting.Select(entry => $"{entry.Key} holds {entry.Value}"));
        return $"{Interlocked.Read(ref _beingHandled)} message(s) being handled, {queues}";
    }
}

/// <summary>A stand-in responder (<see cref="MessageBusTestNetwork.StartResponderAsync{TRequest, TResponse}"/>).</summary>
public sealed class MessageBusStandIn : IAsyncDisposable
{
    private readonly IHost _host;

    internal MessageBusStandIn(IHost host)
        => _host = host;

    /// <summary>The stand-in's own harness: what it consumed and answered.</summary>
    public MessageBusTestHarness Harness
        => _host.Services.GetRequiredService<MessageBusTestHarness>();

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

internal sealed record StandInResponse<TRequest, TResponse>(Func<TRequest, CancellationToken, Task<Result<TResponse>>> Respond)
    where TRequest : class
    where TResponse : class;

internal sealed class StandInResponder<TRequest, TResponse>(StandInResponse<TRequest, TResponse> response)
    : IRequestResponder<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    public Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
        => response.Respond(request, cancellationToken);
}
