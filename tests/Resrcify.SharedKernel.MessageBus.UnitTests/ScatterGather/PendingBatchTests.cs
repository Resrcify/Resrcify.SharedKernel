using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.ScatterGather;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class PendingBatchTests
{
    [Fact]
    public void Record_ShouldAcceptTheFirstReply_WhenAnItemIsAnswered()
        => new PendingBatch(["a", "b"]).Record("a", "first").ShouldBe(ReplyOutcome.Accepted);

    [Fact]
    public void Record_ShouldIgnoreTheSecondReply_WhenAnItemIsAnsweredTwice()
    {
        var batch = new PendingBatch(["a", "b"]);
        batch.Record("a", "first");

        batch.Record("a", "second").ShouldBe(ReplyOutcome.Duplicate);
        batch.ToGathered<string>().Results["a"].ShouldBe("first");
    }

    [Fact]
    public void Record_ShouldIgnoreTheReply_WhenTheItemIsNotInTheBatch()
        => new PendingBatch(["a"]).Record("z", "stray").ShouldBe(ReplyOutcome.Late);

    [Fact]
    public void AllAnswered_ShouldComplete_WhenEveryItemHasAnAnswer()
    {
        var batch = new PendingBatch(["a", "b"]);
        batch.Record("a", "1");
        batch.Record("a", "1 again");
        batch.AllAnswered.IsCompleted.ShouldBeFalse();

        batch.Record("b", "2");

        batch.AllAnswered.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public void ToGathered_ShouldGiveTheRespondersErrors_WhenAnItemFailed()
    {
        var batch = new PendingBatch(["a"]);
        batch.Record("a", ScatterRequestFailed.From([NoData]));

        batch.ToGathered<string>().Failures["a"].ShouldBe([NoData]);
    }

    [Fact]
    public void ToGathered_ShouldFailTheItem_WhenTheReplyIsOfAnotherType()
    {
        var batch = new PendingBatch(["a"]);
        batch.Record("a", 42);

        batch.ToGathered<string>()["a"].Errors.Single().Code.ShouldBe("ScatterGather.UnexpectedReply");
    }

    [Fact]
    public void ToGathered_ShouldThrow_WhenTheBatchIsStreamed()
        => Should.Throw<InvalidOperationException>(() => new PendingBatch(["a"], retainReplies: false).ToGathered<string>());

    [Fact]
    public void Record_ShouldStillIgnoreADuplicate_WhenTheBatchIsStreamed()
    {
        var batch = new PendingBatch(["a", "b"], retainReplies: false);
        batch.Record("a", "first");

        batch.Record("a", "second").ShouldBe(ReplyOutcome.Duplicate);
        batch.Arrivals.TryRead(out var arrival).ShouldBeTrue();
        arrival.Value.ShouldBe("first");
        batch.Arrivals.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public void Arrivals_ShouldThrow_WhenTheBatchIsGathered()
        => Should.Throw<InvalidOperationException>(() => new PendingBatch(["a"]).Arrivals);

    [Fact]
    public void AllAnswered_ShouldComplete_WhenEveryItemAnswersAtOnce()
    {
        // Arrange
        var keys = Enumerable.Range(0, 1_000).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
        var batch = new PendingBatch(keys);

        // Act
        Parallel.ForEach(keys, key => batch.Record(key, key));

        // Assert
        batch.AllAnswered.IsCompleted.ShouldBeTrue();
        batch.ToGathered<string>().Results.Count.ShouldBe(1_000);
    }

    [Fact]
    public async Task Arrivals_ShouldHaveEveryReplyBeforeCompleting_WhenEveryItemAnswersAtOnce()
    {
        // Arrange
        var keys = Enumerable.Range(0, 1_000).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
        var batch = new PendingBatch(keys, retainReplies: false);

        // Act
        Parallel.ForEach(keys, key => batch.Record(key, key));

        // Assert
        var arrived = new List<string>();
        await foreach (var arrival in batch.Arrivals.ReadAllAsync())
            arrived.Add(arrival.Key);
        arrived.Order(StringComparer.Ordinal).ShouldBe(keys.Order(StringComparer.Ordinal));
        batch.AllAnswered.IsCompleted.ShouldBeTrue();
    }

    private static Error NoData => new("Ping.NoData", "No data for this ping", ErrorType.NotFound);
}
