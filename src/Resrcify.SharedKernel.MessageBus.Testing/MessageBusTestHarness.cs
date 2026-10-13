using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.MessageBus.Testing;

/// <summary>
/// One service's view of its buses in a test (<c>AddMessageBusTestHarness</c>): what they published, sent, consumed,
/// failed and dead-lettered, and failures to inject. Resolve it from the service's container.
/// </summary>
/// <remarks>
/// Every bus of the service is recorded: its own, the scatter-gather reply bus and each rate-limited queue's. A message
/// counts as sent or published once it left (its transaction committed): one sent by a handler that then failed isn't.
/// </remarks>
public sealed class MessageBusTestHarness
{
    private readonly Lock _gate = new();
    private readonly List<DeliveryFailure> _deliveryFailures = [];
    private readonly Dictionary<string, int> _startFailures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (object? Body, Exception? Exception)> _inProgress = new(StringComparer.Ordinal);

    internal MessageBusTestHarness(MessageBusTestNetwork network)
        => Network = network;

    /// <summary>The network the service's buses run on, shared with the other services of the test.</summary>
    public MessageBusTestNetwork Network { get; }

    /// <summary>Events the service published.</summary>
    public MessageRecording Published { get; } = new("published");

    /// <summary>Messages the service sent to a queue: requests, replies, commands.</summary>
    public MessageRecording Sent { get; } = new("sent");

    /// <summary>Messages the service's handlers handled without throwing.</summary>
    public MessageRecording Consumed { get; } = new("consumed");

    /// <summary>
    /// Each delivery that threw (a handler, or <see cref="FailNext{TMessage}"/>), retries included: a message that failed
    /// twice and then went through is here twice and in <see cref="Consumed"/> once.
    /// </summary>
    public MessageRecording Faulted { get; } = new("faulted");

    /// <summary>Messages given up on after their last try (moved to the error queue, or dropped where the bus does).</summary>
    public MessageRecording DeadLettered { get; } = new("dead-lettered");

    /// <inheritdoc cref="MessageBusTestNetwork.WaitUntilIdleAsync"/>
    public Task WaitUntilIdleAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => Network.WaitUntilIdleAsync(timeout, cancellationToken);

    /// <summary>
    /// Fails the next <paramref name="deliveries"/> deliveries of a <typeparamref name="TMessage"/> to this service,
    /// before its handler runs, with <paramref name="exception"/> (an <see cref="InvalidOperationException"/> unless
    /// given). The bus retries it as it would after a handler's failure.
    /// </summary>
    public void FailNext<TMessage>(int deliveries = 1, Exception? exception = null)
        where TMessage : class
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(deliveries, 1);
        lock (_gate)
            _deliveryFailures.Add(new DeliveryFailure(
                typeof(TMessage),
                deliveries,
                exception ?? new InvalidOperationException($"{typeof(TMessage).Name} failed (FailNext, the test harness).")));
    }

    /// <summary>
    /// Fails the next <paramref name="starts"/> starts of the bus that consumes <paramref name="queue"/>, as a queue that
    /// can't be declared does (RabbitMQ unreachable, a node down).
    /// </summary>
    public void FailStart(string queue, int starts = 1)
    {
        ArgumentException.ThrowIfNullOrEmpty(queue);
        ArgumentOutOfRangeException.ThrowIfLessThan(starts, 1);
        lock (_gate)
            _startFailures[queue] = starts;
    }

    internal bool TryFailDelivery(object? body, out Exception exception)
    {
        lock (_gate)
        {
            var failure = _deliveryFailures.Find(candidate => candidate.MessageType.IsInstanceOfType(body));
            if (failure is null)
            {
                exception = null!;
                return false;
            }

            failure.Left--;
            if (failure.Left == 0)
                _deliveryFailures.Remove(failure);
            exception = failure.Exception;
            return true;
        }
    }

    internal bool TryFailStart(string? queue)
    {
        if (queue is null)
            return false;
        lock (_gate)
        {
            if (!_startFailures.TryGetValue(queue, out var left))
                return false;
            if (left == 1)
                _startFailures.Remove(queue);
            else
                _startFailures[queue] = left - 1;
            return true;
        }
    }

    // A message being handled, kept until it went through or was given up on, so a dead letter (which the error handler
    // sees only as bytes) is recorded with its message and the last failure.
    internal void Handling(string? messageId, object? body)
    {
        if (messageId is not null)
            _inProgress.AddOrUpdate(messageId, (body, null), (_, previous) => (body, previous.Exception));
    }

    internal void Failed(string? messageId, Exception exception)
    {
        if (messageId is not null)
            _inProgress.AddOrUpdate(messageId, (null, exception), (_, previous) => (previous.Body, exception));
    }

    internal void Done(string? messageId)
    {
        if (messageId is not null)
            _inProgress.TryRemove(messageId, out _);
    }

    internal (object? Body, Exception? Exception) GiveUp(string? messageId)
        => messageId is not null && _inProgress.TryRemove(messageId, out var last) ? last : (null, null);

    private sealed class DeliveryFailure(Type messageType, int left, Exception exception)
    {
        public Type MessageType { get; } = messageType;

        public int Left { get; set; } = left;

        public Exception Exception { get; } = exception;
    }
}
