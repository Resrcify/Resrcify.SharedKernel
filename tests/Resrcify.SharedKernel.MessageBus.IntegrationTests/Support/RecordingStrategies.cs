using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Rebus.Config;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;

/// <summary>Counts how many buses it was asked to configure; changes nothing.</summary>
internal sealed class RecordingBusConfigurationStrategy : IBusConfigurationStrategy
{
    private int _transports;
    private int _options;

    public int Transports => Volatile.Read(ref _transports);
    public int Options => Volatile.Read(ref _options);

    public void ConfigureTransport(RabbitMqOptionsBuilder transport)
        => Interlocked.Increment(ref _transports);

    public void ConfigureOptions(OptionsConfigurer options)
        => Interlocked.Increment(ref _options);
}

/// <summary>The default token bucket, counting the leases it grants and the queues it was created for.</summary>
internal sealed class CountingRateLimiterStrategy : IRateLimiterStrategy
{
    private readonly ConcurrentQueue<string> _queues = new();
    private int _acquired;

    public IReadOnlyCollection<string> Queues => _queues;
    public int Acquired => Volatile.Read(ref _acquired);

    public RateLimiter CreateLimiter(string queueName, RateLimitedQueueOptions options)
    {
        _queues.Enqueue(queueName);
        return new CountingRateLimiter(
            TokenBucketRateLimiterStrategy.Instance.CreateLimiter(queueName, options),
            () => Interlocked.Increment(ref _acquired));
    }

    private sealed class CountingRateLimiter(RateLimiter inner, Action onAcquired)
        : RateLimiter
    {
        public override TimeSpan? IdleDuration => inner.IdleDuration;

        public override RateLimiterStatistics? GetStatistics()
            => inner.GetStatistics();

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
            => Count(inner.AttemptAcquire(permitCount));

        protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
            => Count(await inner.AcquireAsync(permitCount, cancellationToken));

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }

        private RateLimitLease Count(RateLimitLease lease)
        {
            if (lease.IsAcquired)
                onAcquired();
            return lease;
        }
    }
}
