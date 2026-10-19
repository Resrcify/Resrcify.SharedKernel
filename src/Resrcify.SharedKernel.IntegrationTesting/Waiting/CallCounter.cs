using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.IntegrationTesting.Waiting;

/// <summary>
/// Counts calls a stand-in received, and tells a test when the count reached a number: a signal to wait on instead of
/// a delay, so the test does its next step exactly when the call it waits for has been made. Safe to increment from any
/// thread; a waiter's continuation never runs inside <see cref="Increment"/>.
/// </summary>
public sealed class CallCounter
{
    private readonly Lock _gate = new();
    private readonly List<(int Count, TaskCompletionSource Reached)> _waiters = [];
    private int _count;

    /// <summary>The calls made so far.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _count;
        }
    }

    /// <summary>Counts a call, and completes every wait for a count it reached.</summary>
    public void Increment()
    {
        List<TaskCompletionSource> reached = [];
        lock (_gate)
        {
            _count++;
            foreach (var waiter in _waiters.FindAll(waiter => waiter.Count <= _count))
            {
                _waiters.Remove(waiter);
                reached.Add(waiter.Reached);
            }
        }

        foreach (var waiter in reached)
            waiter.TrySetResult();
    }

    /// <summary>Completes once <paramref name="count"/> calls were made: at once when they already were.</summary>
    public Task ReachedAsync(int count)
    {
        lock (_gate)
        {
            if (_count >= count)
                return Task.CompletedTask;

            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, reached));
            return reached.Task;
        }
    }
}
