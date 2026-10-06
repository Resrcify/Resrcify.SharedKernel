using System;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Resrcify.SharedKernel.MessageBus.Abstractions;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>
/// One budget for several queues: every queue that uses this strategy takes its leases from the same limiter,
/// so together they stay within it on each instance (e.g. several request types hitting the same upstream).
/// Each queue's own <see cref="RateLimitedQueueOptions.PerSecond"/> and <see cref="RateLimitedQueueOptions.Burst"/>
/// are then not used, so set its <see cref="RateLimitedQueueOptions.Prefetch"/> explicitly.
/// </summary>
/// <remarks>
/// The limiter lives while at least one queue's bus runs: it is created when the first one starts and disposed
/// when the last one stops (e.g. every queue gated off by its health check, or shutdown), so the strategy itself
/// needs no disposing.
/// </remarks>
/// <example>
/// <code>
/// var gameBudget = SharedRateLimiterStrategy.TokenBucket(perSecond: 18, burst: 2);
/// bus.AddRateLimitedQueue&lt;GuildRequest, GuildResponse, GuildHandler&gt;("swgohapi.guild", queue => queue.RateLimiter = gameBudget)
///    .AddRateLimitedQueue&lt;LeaderboardRequest, LeaderboardResponse, LeaderboardHandler&gt;("swgohapi.leaderboard", queue => queue.RateLimiter = gameBudget);
/// </code>
/// </example>
/// <param name="createLimiter">Creates the shared limiter (again, after every queue has stopped).</param>
public sealed class SharedRateLimiterStrategy(Func<RateLimiter> createLimiter) : IRateLimiterStrategy
{
    private readonly Lock _gate = new();
    private RateLimiter? _limiter;
    private int _queues;

    /// <summary>A shared token bucket: <paramref name="perSecond"/> requests a second, bursts of up to <paramref name="burst"/>.</summary>
    public static SharedRateLimiterStrategy TokenBucket(int perSecond, int burst)
        => new(() => TokenBucketRateLimiterStrategy.Instance.CreateLimiter(
            "shared",
            new RateLimitedQueueOptions { PerSecond = perSecond, Burst = burst }));

    /// <summary>
    /// A view of the shared limiter for one queue's bus. Disposing it (when that bus stops) ends the budget only
    /// once no other queue uses it.
    /// </summary>
    public RateLimiter CreateLimiter(string queueName, RateLimitedQueueOptions options)
    {
        lock (_gate)
        {
            _limiter ??= createLimiter();
            _queues++;
            return new SharedLimiterView(_limiter, Release);
        }
    }

    private void Release()
    {
        lock (_gate)
        {
            if (--_queues > 0)
                return;
            _limiter?.Dispose();
            _limiter = null;
        }
    }

    private sealed class SharedLimiterView(RateLimiter shared, Action release) : RateLimiter
    {
        private int _released;

        public override TimeSpan? IdleDuration => shared.IdleDuration;

        public override RateLimiterStatistics? GetStatistics()
            => shared.GetStatistics();

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
            => shared.AttemptAcquire(permitCount);

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
            => shared.AcquireAsync(permitCount, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _released, 1) == 0)
                release();
            base.Dispose(disposing);
        }
    }
}
