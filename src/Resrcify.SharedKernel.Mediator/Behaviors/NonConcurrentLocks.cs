using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>
/// The locks of the non-concurrent requests (a singleton, registered with the mediator): one per request type and
/// concurrency key, held by the request being handled and waited for by the others.
/// </summary>
/// <remarks>
/// A lock exists only while a request holds it or waits for it: the last one to leave removes it. Keys can therefore be
/// as many as the requests' data makes (a shard's ID, a user's): the locks kept are those in use, never every key seen.
/// </remarks>
internal sealed class NonConcurrentLocks
{
    private readonly Lock _gate = new();
    private readonly Dictionary<LockKey, KeyLock> _locks = [];

    /// <summary>
    /// Waits until the lock of <paramref name="requestType"/> and <paramref name="concurrencyKey"/> is free, and takes it;
    /// disposing the returned lease frees it.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled before the lock was taken: it isn't held.
    /// </exception>
    public async Task<IDisposable> AcquireAsync(
        Type requestType,
        string? concurrencyKey,
        CancellationToken cancellationToken)
    {
        var key = new LockKey(
            requestType,
            string.IsNullOrEmpty(concurrencyKey) ? null : concurrencyKey);
        var keyLock = Join(key);
        try
        {
            await keyLock.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            Leave(key, keyLock);
            throw;
        }

        return new Lease(this, key, keyLock);
    }

    /// <summary>The number of locks held or waited for: those kept.</summary>
    internal int Count
    {
        get
        {
            lock (_gate)
                return _locks.Count;
        }
    }

    private KeyLock Join(LockKey key)
    {
        lock (_gate)
        {
            if (!_locks.TryGetValue(key, out var keyLock))
            {
                keyLock = new KeyLock();
                _locks[key] = keyLock;
            }

            keyLock.Users++;
            return keyLock;
        }
    }

    // A request leaves after releasing the semaphore (or without having taken it): when it was the last, nobody holds the
    // semaphore or waits for it, so it can go. One that arrives afterwards makes a new one.
    private void Leave(
        LockKey key,
        KeyLock keyLock)
    {
        lock (_gate)
        {
            keyLock.Users--;
            if (keyLock.Users > 0)
                return;

            _locks.Remove(key);
            keyLock.Dispose();
        }
    }

    private readonly record struct LockKey(
        Type RequestType,
        string? ConcurrencyKey);

    private sealed class KeyLock
        : IDisposable
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>The requests holding it or waiting for it; read and written under the registry's gate.</summary>
        public int Users { get; set; }

        public void Dispose()
            => Semaphore.Dispose();
    }

    private sealed class Lease(
        NonConcurrentLocks locks,
        LockKey key,
        KeyLock keyLock)
        : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1)
                return;

            keyLock.Semaphore.Release();
            locks.Leave(key, keyLock);
        }
    }
}
