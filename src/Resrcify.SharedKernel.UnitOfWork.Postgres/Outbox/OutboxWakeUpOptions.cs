using System;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Outbox;

/// <summary>How the outbox wake-up listens, set with <c>WithOutboxWakeUp</c>.</summary>
public sealed class OutboxWakeUpOptions
{
    /// <summary>The PostgreSQL channel the saves notify and the listener listens on; the payload names the DbContext.</summary>
    public const string Channel = "resrcify_outbox";

    /// <summary>
    /// How long the listener waits after a notification before it wakes the outbox (50 ms by default), so a burst of
    /// saves wakes it once. Zero wakes it at once.
    /// </summary>
    public TimeSpan Debounce { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>How long the listener waits before its first reconnect after losing its connection (1 s by default).</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait between reconnects; the wait doubles up to it (30 s by default).</summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Seconds of silence after which Npgsql checks the listener's connection (its <c>Keepalive</c>), so a connection
    /// that died without a word is noticed and replaced (30 by default).
    /// </summary>
    public int KeepAliveInSeconds { get; set; } = 30;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Debounce, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ReconnectDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxReconnectDelay, ReconnectDelay);
        ArgumentOutOfRangeException.ThrowIfLessThan(KeepAliveInSeconds, 1);
    }
}
