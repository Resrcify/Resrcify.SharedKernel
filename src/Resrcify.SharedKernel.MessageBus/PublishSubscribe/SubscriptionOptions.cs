using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>Whether a subscription handles its events in publish order (see <see cref="SubscriptionOptions{TEvent}"/>).</summary>
internal interface IPublishOrderedSubscription
{
    bool InPublishOrder { get; }
}

/// <summary>
/// How a service handles one event it subscribes to. Set with <c>MessageBusBuilder.ConfigureSubscription</c>; by
/// default events are handled concurrently, up to the input queue's parallelism.
/// </summary>
public sealed class SubscriptionOptions<TEvent>
    : IPublishOrderedSubscription
    where TEvent : class
{
    private Func<TEvent, object>? _partitionKey;
    private Partition[] _partitions = [];
    private bool PublishOrderRequested { get; set; }

    /// <summary>
    /// Handles events with the same key one at a time, in the order they were published; different keys run
    /// concurrently. Like MassTransit's <c>UsePartitioner(partitions, key)</c>: keys share
    /// <paramref name="partitions"/> lanes by hash, so two keys can occasionally wait for each other.
    /// </summary>
    /// <remarks>
    /// <para>In publish order (<paramref name="inPublishOrder"/>, the default):</para>
    /// <list type="bullet">
    /// <item>an event takes its place in its partition in the order the bus received it, which is the order it was
    /// published in;</item>
    /// <item>a failing event is retried in its place (5 tries, waiting 0.5, 1, 2 and 4 s) and then moved to the error
    /// queue, so the events after it of the same partition wait meanwhile and run after it;</item>
    /// <item>on RabbitMQ, only one instance of the service consumes its input queue at a time ("single active
    /// consumer"); the others take over if it stops. Instances of the service otherwise share the work, and two of
    /// them could handle events of one key at the same time.</item>
    /// </list>
    /// <para>
    /// Order is the order the events reached the broker: events published at the same time by two instances of the
    /// publisher have no order between them. An event that ends in the error queue is skipped.
    /// </para>
    /// <para>
    /// Single active consumer is a property of the queue: a queue that already exists without it can't get it. Delete
    /// the (drained) queue, or use a new input queue name, when turning publish order on for a service in production.
    /// </para>
    /// </remarks>
    /// <example><c>options.HandleInPartitions(e =&gt; e.ShardId)</c></example>
    public SubscriptionOptions<TEvent> HandleInPartitions(
        Func<TEvent, object> key,
        int partitions = 16,
        bool inPublishOrder = true)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(partitions, 1);
        _partitionKey = key;
        _partitions = new Partition[partitions];
        for (var i = 0; i < partitions; i++)
            _partitions[i] = new Partition();
        PublishOrderRequested = inPublishOrder;
        return this;
    }

    /// <summary>
    /// Handles this event one at a time on each instance, in publish order unless <paramref name="inPublishOrder"/> is
    /// false (like a MassTransit consumer's <c>ConcurrentMessageLimit = 1</c>). See <see cref="HandleInPartitions"/>.
    /// </summary>
    public SubscriptionOptions<TEvent> HandleOneAtATime(bool inPublishOrder = true)
        => HandleInPartitions(_ => 0, partitions: 1, inPublishOrder);

    /// <summary>Whether events are handled in publish order: partitioned, and not opted out.</summary>
    public bool InPublishOrder
        => _partitionKey is not null && PublishOrderRequested;

    /// <summary>
    /// Runs <paramref name="handle"/> in the event's partition. The event takes its place in the partition before this
    /// returns (synchronously), so callers can order events by the order they call it in.
    /// </summary>
    internal Task RunAsync(TEvent integrationEvent, Func<Task> handle)
    {
        if (_partitionKey is null)
            return handle();
        var key = _partitionKey(integrationEvent);
        var index = (int)((uint)(key?.GetHashCode() ?? 0) % (uint)_partitions.Length);
        return _partitions[index].RunAsync(handle);
    }

    /// <summary>
    /// Runs work one at a time, in the order it reached the partition. When work leaves by an exception (its event goes
    /// back to the queue, to be delivered again), the work already waiting behind it goes back too, without running:
    /// handled now, it would come before the event it followed, whose redelivery is received after it. Requeued in order,
    /// they come back in order. Work arriving after that runs as usual.
    /// </summary>
    private sealed class Partition
    {
        private Task _tail = Task.CompletedTask;
        private long _issued;
        private long _sendBackThrough;

        public async Task RunAsync(Func<Task> work)
        {
            var ticket = Interlocked.Increment(ref _issued);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var previous = Interlocked.Exchange(ref _tail, done.Task);
            try
            {
                await previous;
                if (ticket <= Volatile.Read(ref _sendBackThrough))
                    throw new SentBackBehindException();
                await RunWorkAsync(work);
            }
            finally
            {
                done.SetResult();
            }
        }

        private async Task RunWorkAsync(Func<Task> work)
        {
            try
            {
                await work();
            }
            catch
            {
                // Everything queued behind it so far follows it back.
                Volatile.Write(ref _sendBackThrough, Volatile.Read(ref _issued));
                throw;
            }
        }
    }
}
