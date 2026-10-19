namespace Resrcify.SharedKernel.Abstractions.Mediator;

/// <summary>
/// A request that runs one at a time: while one is handled, another of the same request type and
/// <see cref="ConcurrencyKey"/> waits until it has finished, and is then handled itself (the mediator's non-concurrent
/// behavior). A job command, say, so that a scheduled run and one started on demand never overlap.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>It serialises; it doesn't coalesce. The request that waited is handled too, after the first, and gets its own
/// result, not the first one's. To answer a repeat with the first result, make it an <see cref="IIdempotentRequest"/>
/// too.</item>
/// <item>It holds per process. Two instances (replicas) of a service each run the request one at a time, but not one at
/// a time between them: what must never overlap across instances needs a lock they share (the database's).</item>
/// <item>It waits; it doesn't refuse. Nothing times out on its own: a caller that can't wait for long sends the request
/// with a <see cref="System.Threading.CancellationToken"/>. Cancelled while it waits, the request isn't handled and the
/// send throws <see cref="System.OperationCanceledException"/>.</item>
/// <item>It isn't re-entrant: a handler that sends (and awaits) a request of its own type and key waits for itself, for
/// ever.</item>
/// </list>
/// </remarks>
public interface INonConcurrentRequest
{
    /// <summary>
    /// What must not run at the same time, within the request type; <see langword="null"/> or empty: every run of the
    /// request type, one at a time. A key narrows it: runs with different keys (e.g. each shard's ID) are handled side
    /// by side, runs with the same key one at a time. The runs without a key and those with one don't wait for each
    /// other.
    /// </summary>
    string? ConcurrencyKey => null;
}
