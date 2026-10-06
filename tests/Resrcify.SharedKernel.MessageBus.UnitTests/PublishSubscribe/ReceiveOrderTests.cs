using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.MessageBus.PublishSubscribe;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.PublishSubscribe;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ReceiveOrderTests
{
    [Fact]
    public async Task WaitForTurnAsync_ShouldCompleteAtOnce_WhenItIsTheFirstMessage()
    {
        var order = new ReceiveOrder();

        var turn = order.WaitForTurnAsync(1, CancellationToken.None);

        turn.IsCompletedSuccessfully.ShouldBeTrue();
        await turn;
    }

    [Fact]
    public async Task WaitForTurnAsync_ShouldWait_UntilEveryEarlierMessageIsAdmitted()
    {
        var order = new ReceiveOrder();

        var turn = order.WaitForTurnAsync(3, CancellationToken.None);
        order.Admit(2);
        await Task.Delay(50);
        turn.IsCompleted.ShouldBeFalse();   // 1 is still out

        order.Admit(1);
        await turn.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitForTurnAsync_ShouldCompleteAtOnce_WhenEarlierMessagesWereAdmittedOutOfOrder()
    {
        var order = new ReceiveOrder();
        order.Admit(2);
        order.Admit(1);

        var turn = order.WaitForTurnAsync(3, CancellationToken.None);

        turn.IsCompletedSuccessfully.ShouldBeTrue();
        await turn;
    }

    [Fact]
    public async Task Admit_ShouldDoNothing_WhenTheMessageWasAdmittedAlready()
    {
        var order = new ReceiveOrder();
        order.Admit(1);
        order.Admit(1);

        var turn = order.WaitForTurnAsync(3, CancellationToken.None);
        await Task.Delay(50);

        turn.IsCompleted.ShouldBeFalse();   // 2 hasn't been admitted: admitting 1 twice doesn't count for it
        order.Admit(2);
        await turn.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitForTurnAsync_ShouldStop_WhenCancelled()
    {
        var order = new ReceiveOrder();
        using var cancellation = new CancellationTokenSource();

        var turn = order.WaitForTurnAsync(2, cancellation.Token);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => turn);
    }

    [Fact]
    public async Task WaitForTurnAsync_ShouldGiveUpWaiting_WhenAnEarlierMessageIsNeverAdmitted()
    {
        var clock = new FakeTimeProvider();
        var order = new ReceiveOrder(clock);

        var turn = order.WaitForTurnAsync(2, CancellationToken.None);   // 1 never comes
        clock.Advance(ReceiveOrder.LongestWait - TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        turn.IsCompleted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromSeconds(1));
        await turn.WaitAsync(TimeSpan.FromSeconds(5));   // goes on rather than hold the queue up
    }
}
