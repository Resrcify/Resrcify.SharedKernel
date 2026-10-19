using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.IntegrationTesting.Waiting;

/// <summary>
/// What a recorder or spy saw, in the order it saw it, and a signal for each wait: <see cref="WaitForAsync"/> completes
/// as soon as a matching entry is added (at once if there is one), so a test waits for exactly what it expects instead
/// of polling against a deadline. Safe to add to from any thread; a waiter's continuation never runs inside
/// <see cref="Add"/>.
/// </summary>
/// <typeparam name="T">What is recorded (a request, a message, a call's arguments).</typeparam>
public sealed class AwaitableLog<T>
{
    private readonly Lock _gate = new();
    private readonly List<T> _entries = [];
    private readonly List<(Func<T, bool> Matches, TaskCompletionSource<T> Found)> _waiters = [];

    /// <summary>Everything added so far, oldest first.</summary>
    public IReadOnlyList<T> Entries
    {
        get
        {
            lock (_gate)
                return [.. _entries];
        }
    }

    /// <summary>Records <paramref name="entry"/> and completes every wait it matches.</summary>
    public void Add(T entry)
    {
        List<TaskCompletionSource<T>> found = [];
        lock (_gate)
        {
            _entries.Add(entry);
            foreach (var waiter in _waiters.FindAll(waiter => waiter.Matches(entry)))
            {
                _waiters.Remove(waiter);
                found.Add(waiter.Found);
            }
        }

        foreach (var waiter in found)
            waiter.TrySetResult(entry);
    }

    /// <summary>The first entry <paramref name="matches"/> accepts: at once if one was added, or as soon as one is.</summary>
    public Task<T> WaitForAsync(Func<T, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        lock (_gate)
        {
            var index = _entries.FindIndex(entry => matches(entry));
            if (index >= 0)
                return Task.FromResult(_entries[index]);

            var found = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((matches, found));
            return found.Task;
        }
    }
}
