using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class WakeSignalTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMinutes(1);

    private readonly FakeTimeProvider _clock = new();
    private readonly WakeSignal _signal = new();

    [Fact]
    public async Task WaitAsync_ShouldReturnAtOnce_WhenWokenWhileTheLoopWasBusy()
    {
        _signal.Set();

        var waiting = _signal.WaitAsync(Poll, _clock, CancellationToken.None);
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        waiting.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task WaitAsync_ShouldReturn_WhenWokenDuringTheWait()
    {
        var waiting = _signal.WaitAsync(Poll, _clock, CancellationToken.None);
        waiting.IsCompleted.ShouldBeFalse();

        _signal.Set();

        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitAsync_ShouldReturn_WhenThePollIntervalPasses()
    {
        var waiting = _signal.WaitAsync(Poll, _clock, CancellationToken.None);

        _clock.Advance(Poll);

        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        waiting.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task WaitAsync_ShouldWaitAgain_AfterAWakeUpWasTaken()
    {
        _signal.Set();
        await _signal.WaitAsync(Poll, _clock, CancellationToken.None);

        var waiting = _signal.WaitAsync(Poll, _clock, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        waiting.IsCompleted.ShouldBeFalse();
        _signal.Set();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitAsync_ShouldThrow_WhenCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var waiting = _signal.WaitAsync(Poll, _clock, cancellation.Token);

        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => waiting);
    }
}
