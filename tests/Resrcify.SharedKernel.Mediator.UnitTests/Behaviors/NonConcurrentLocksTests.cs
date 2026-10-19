using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Behaviors;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class NonConcurrentLocksTests
{
    private readonly NonConcurrentLocks _locks = new();

    [Fact]
    public async Task AcquireAsync_ShouldWait_UntilTheHolderOfTheSameKeyReleasesIt()
    {
        var first = await _locks.AcquireAsync(typeof(IFirstRequest), "shard-1", CancellationToken.None);

        var second = _locks.AcquireAsync(typeof(IFirstRequest), "shard-1", CancellationToken.None);

        second.IsCompleted.ShouldBeFalse();
        first.Dispose();
        using var secondLease = await second;
        _locks.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AcquireAsync_ShouldNotWait_ForAnotherKeyOrRequestType()
    {
        using var held = await _locks.AcquireAsync(typeof(IFirstRequest), "shard-1", CancellationToken.None);

        var otherKey = _locks.AcquireAsync(typeof(IFirstRequest), "shard-2", CancellationToken.None);
        var otherType = _locks.AcquireAsync(typeof(ISecondRequest), "shard-1", CancellationToken.None);
        var withoutAKey = _locks.AcquireAsync(typeof(IFirstRequest), null, CancellationToken.None);

        otherKey.IsCompletedSuccessfully.ShouldBeTrue();
        otherType.IsCompletedSuccessfully.ShouldBeTrue();
        withoutAKey.IsCompletedSuccessfully.ShouldBeTrue();
        (await otherKey).Dispose();
        (await otherType).Dispose();
        (await withoutAKey).Dispose();
    }

    [Fact]
    public async Task AcquireAsync_ShouldTakeAnEmptyKeyAsNone()
    {
        using var withoutAKey = await _locks.AcquireAsync(typeof(IFirstRequest), null, CancellationToken.None);

        var empty = _locks.AcquireAsync(typeof(IFirstRequest), string.Empty, CancellationToken.None);

        empty.IsCompleted.ShouldBeFalse();
        _locks.Count.ShouldBe(1);
        withoutAKey.Dispose();
        (await empty).Dispose();
    }

    [Fact]
    public async Task AcquireAsync_ShouldNotTakeTheLock_WhenCancelledWhileWaiting()
    {
        var first = await _locks.AcquireAsync(typeof(IFirstRequest), "shard-1", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var cancelled = _locks.AcquireAsync(typeof(IFirstRequest), "shard-1", cancellation.Token);

        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => cancelled);
        first.Dispose();
        _locks.Count.ShouldBe(0);
        using var next = await _locks.AcquireAsync(typeof(IFirstRequest), "shard-1", CancellationToken.None);
    }

    [Fact]
    public async Task AcquireAsync_ShouldThrow_WhenAlreadyCancelled_ThoughTheLockIsFree()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => _locks.AcquireAsync(typeof(IFirstRequest), "shard-1", cancellation.Token));

        _locks.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ALock_ShouldBeRemoved_WhenNobodyHoldsOrWaitsForIt()
    {
        // Keys can be as many as the requests' data makes: only the locks in use are kept.
        for (var shard = 0; shard < 100; shard++)
            (await _locks.AcquireAsync(typeof(IFirstRequest), $"shard-{shard}", CancellationToken.None)).Dispose();

        _locks.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ALease_ShouldReleaseOnce_WhenDisposedTwice()
    {
        var first = await _locks.AcquireAsync(typeof(IFirstRequest), "shard-1", CancellationToken.None);
        var second = _locks.AcquireAsync(typeof(IFirstRequest), "shard-1", CancellationToken.None);
        var third = _locks.AcquireAsync(typeof(IFirstRequest), "shard-1", CancellationToken.None);

        first.Dispose();
        first.Dispose();

        using var secondLease = await second;
        third.IsCompleted.ShouldBeFalse();
        secondLease.Dispose();
        (await third).Dispose();
        _locks.Count.ShouldBe(0);
    }

    private interface IFirstRequest;

    private interface ISecondRequest;
}
