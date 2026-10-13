using System;
using System.Collections.Concurrent;
using Quartz;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// When each Quartz job was last seen alive: when it last fired or finished (succeeded or not). A job whose heartbeat
/// goes stale has stopped firing, which only a restart fixes (Shard's rank updates froze for ~29 h that way). A job
/// that fires but fails (its upstream is down) keeps beating, so it doesn't look frozen and cause a restart loop.
/// </summary>
/// <remarks>Fed by <see cref="JobHeartbeatSetup.AddJobHeartbeats"/>; read by the <c>AddQuartzJobs</c> health check.</remarks>
public sealed class JobHeartbeatRegistry
{
    private readonly ConcurrentDictionary<JobKey, DateTimeOffset> _lastBeat = new();
    private readonly TimeProvider _time;

    public JobHeartbeatRegistry(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        StartedAt = _time.GetUtcNow();
    }

    /// <summary>When the registry was made (the process started): the reference for a job that hasn't fired yet.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>Records that <paramref name="job"/> fired or finished now.</summary>
    public void Beat(JobKey job)
    {
        ArgumentNullException.ThrowIfNull(job);
        _lastBeat[job] = _time.GetUtcNow();
    }

    /// <summary>When <paramref name="job"/> last fired or finished; <see langword="null"/> when it hasn't since start.</summary>
    public DateTimeOffset? LastBeat(JobKey job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return _lastBeat.TryGetValue(job, out var last) ? last : null;
    }
}
