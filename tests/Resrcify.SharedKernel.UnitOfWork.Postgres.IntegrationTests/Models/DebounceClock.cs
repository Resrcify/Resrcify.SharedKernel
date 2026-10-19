using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.IntegrationTesting.Waiting;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;

/// <summary>
/// The host's clock, held still: the outbox listener's debounce (a wait of <paramref name="debounce"/> on the
/// <see cref="TimeProvider"/>) ends only when the test says (<see cref="EndDebounce"/>), and the test learns when the
/// listener started one (<see cref="DebouncingAsync"/>). So everything sent while a debounce is pending arrives within
/// it, however slowly the machine delivers it. Pick a debounce no other timer of the host uses.
/// </summary>
internal sealed class DebounceClock(TimeSpan debounce) : TimeProvider
{
    private readonly FakeTimeProvider _time = new(TimeProvider.System.GetUtcNow());
    private readonly CallCounter _debounces = new();

    public override TimeZoneInfo LocalTimeZone
        => _time.LocalTimeZone;

    public override long TimestampFrequency
        => _time.TimestampFrequency;

    /// <summary>Completes once the listener started its <paramref name="count"/>th debounce.</summary>
    public Task DebouncingAsync(int count)
        => _debounces.ReachedAsync(count);

    /// <summary>Ends the pending debounce (and fires whatever else is due by then).</summary>
    public void EndDebounce()
        => _time.Advance(debounce);

    public override DateTimeOffset GetUtcNow()
        => _time.GetUtcNow();

    public override long GetTimestamp()
        => _time.GetTimestamp();

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        var timer = _time.CreateTimer(callback, state, dueTime, period);
        // After the timer exists: once the test hears of the debounce, ending it fires the timer.
        if (dueTime == debounce)
            _debounces.Increment();
        return timer;
    }
}
