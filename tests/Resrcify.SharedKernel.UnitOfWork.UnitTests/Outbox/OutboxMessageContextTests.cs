using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Outbox;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxMessageContextTests
{
    private static readonly OutboxMessageContext Context = OutboxMessageContext.Instance;

    [Fact]
    public void NextStableId_ShouldGiveTheSameIds_WhenTheSameMessageIsHandledAgain()
    {
        var messageId = Guid.NewGuid();

        var first = IdsFromOneAttempt(messageId);
        var retry = IdsFromOneAttempt(messageId);

        retry.ShouldBe(first);
        first.ShouldBeUnique();
    }

    [Fact]
    public void NextStableId_ShouldGiveOtherIds_WhenAnotherMessageIsHandled()
        => IdsFromOneAttempt(Guid.NewGuid()).ShouldNotBe(IdsFromOneAttempt(Guid.NewGuid()));

    [Fact]
    public void NextStableId_ShouldCountEachKindOnItsOwn_WhenKindsAreInterleavedDifferently()
    {
        var messageId = Guid.NewGuid();
        Guid? rankFirst;
        Guid? nameAfterRank;
        using (OutboxMessageContext.Enter(messageId))
        {
            rankFirst = Context.NextStableId("RankChanged");
            nameAfterRank = Context.NextStableId("NameChanged");
        }

        using (OutboxMessageContext.Enter(messageId))
        {
            Context.NextStableId("NameChanged").ShouldBe(nameAfterRank);
            Context.NextStableId("RankChanged").ShouldBe(rankFirst);
        }
    }

    [Fact]
    public void NextStableId_ShouldBeNull_OutsideOutboxProcessing()
    {
        Context.MessageId.ShouldBeNull();
        Context.NextStableId("RankChanged").ShouldBeNull();
    }

    [Fact]
    public async Task MessageId_ShouldFlowIntoAsyncCalls_AndBeGoneAfterTheScope()
    {
        var messageId = Guid.NewGuid();
        Guid? seen;
        using (OutboxMessageContext.Enter(messageId))
            seen = await Task.Run(() => Context.MessageId);

        seen.ShouldBe(messageId);
        Context.MessageId.ShouldBeNull();
    }

    private static Guid?[] IdsFromOneAttempt(Guid messageId)
    {
        using (OutboxMessageContext.Enter(messageId))
            return [Context.NextStableId("RankChanged"), Context.NextStableId("RankChanged"), Context.NextStableId("NameChanged")];
    }
}
