using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

/// <summary>
/// Lets a polling loop wait for its next poll or for a wake-up, whichever comes first. A wake-up while the loop is busy
/// isn't lost: the next wait returns at once.
/// </summary>
internal sealed class WakeSignal
{
    private TaskCompletionSource _woken = NewSource();

    /// <summary>Wakes the loop: its current (or next) wait returns.</summary>
    public void Set()
        => Volatile.Read(ref _woken).TrySetResult();

    /// <summary>
    /// Waits until <see cref="Set"/> is called (or was, since the last wait) or <paramref name="timeout"/> passes on
    /// <paramref name="time"/>. Throws <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/>
    /// is cancelled.
    /// </summary>
    public async Task WaitAsync(
        TimeSpan timeout,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var woken = Volatile.Read(ref _woken);
        if (!woken.Task.IsCompleted)
        {
            using var stopDelay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(timeout, time, stopDelay.Token);
            await Task.WhenAny(delay, woken.Task).ConfigureAwait(false);
            await stopDelay.CancelAsync().ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Re-armed before the loop polls, so a wake-up during the poll makes the next wait return at once.
        Interlocked.CompareExchange(ref _woken, NewSource(), woken);
    }

    private static TaskCompletionSource NewSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
