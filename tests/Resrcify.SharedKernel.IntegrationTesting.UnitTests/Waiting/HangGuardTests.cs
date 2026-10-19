using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Resrcify.SharedKernel.IntegrationTesting.Waiting;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.UnitTests.Waiting;

/// <summary>
/// Deterministic throughout: a signal that never comes is a <see cref="TaskCompletionSource"/> never completed, and a
/// hang is forced with a zero limit, so no test waits for a clock.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class HangGuardTests
{
    [Fact]
    public async Task AwaitAsync_ShouldReturnTheSignalsResult_WhenItCompletes()
    {
        var signal = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = HangGuard.AwaitAsync(signal.Task, "the reply");

        signal.SetResult("pong");

        (await waiting).ShouldBe("pong");
    }

    [Fact]
    public async Task AwaitAsync_ShouldSayWhatItWaitedForAndAddTheDiagnostics_WhenTheSignalHangs()
    {
        var never = new TaskCompletionSource<string>();

        var hung = await Should.ThrowAsync<TimeoutException>(() => HangGuard.AwaitAsync(
            never.Task,
            "the reply to ping",
            () => Task.FromResult("container log: exited 137"),
            limit: TimeSpan.Zero));

        hung.Message.ShouldStartWith("Hung: waited 00:00:00 for the reply to ping");
        hung.Message.ShouldEndWith("container log: exited 137");
    }

    [Fact]
    public async Task AwaitAsync_ShouldFailAsHung_WhenAnUntypedSignalHangs()
    {
        var never = new TaskCompletionSource();

        await Should.ThrowAsync<TimeoutException>(() => HangGuard.AwaitAsync(never.Task, "the start", limit: TimeSpan.Zero));
    }

    [Fact]
    public async Task AwaitAsync_ShouldRethrowTheSignalsOwnFailure_RatherThanCallItAHang()
    {
        var failed = Task.FromException(new InvalidOperationException("broker refused"));

        (await Should.ThrowAsync<InvalidOperationException>(() => HangGuard.AwaitAsync(failed, "the publish")))
            .Message.ShouldBe("broker refused");
    }

    [Fact]
    public async Task UntilAsync_ShouldCheckAgain_UntilTheConditionHolds()
    {
        var checks = 0;

        await HangGuard.UntilAsync(
            () => Task.FromResult(++checks == 3),
            "the third check",
            checkEvery: TimeSpan.Zero);

        checks.ShouldBe(3);
    }

    [Fact]
    public async Task UntilAsync_ShouldFailAsHung_WhenTheConditionNeverHolds()
    {
        var hung = await Should.ThrowAsync<TimeoutException>(() => HangGuard.UntilAsync(
            () => Task.FromResult(false),
            "the database to accept connections",
            () => Task.FromResult("last state: starting"),
            limit: TimeSpan.Zero));

        hung.Message.ShouldContain("for the database to accept connections");
        hung.Message.ShouldEndWith("last state: starting");
    }

    [Fact]
    public void Limit_ShouldBeTenMinutes()
        => HangGuard.Limit.ShouldBe(TimeSpan.FromMinutes(10));
}
