using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Testing;
using Resrcify.SharedKernel.MessageBus.UnitTests.ScatterGather;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Testing;

/// <summary>
/// The scatter-gather test double, on a fake clock the tests move: nothing here waits on real time.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ScatterGatherClientStandInTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly Error NotFound = Error.NotFound("RECORDNOTFOUND", "No such guild.");
    private static readonly Error Down = Error.ExternalFailure("SERVERUNAVAILABLE", "The game is down.");

    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task GatherAsync_ShouldAnswerEachItemAsScriptedForItsRequestTypeAndKey()
    {
        var standIn = new ScatterGatherClientStandIn(_time)
            .Answer<GuildQuestion, GuildAnswer>("a", new GuildAnswer("guild a"))
            .Answer<PlayerQuestion, GuildAnswer>("a", new GuildAnswer("player a"))
            .AnswerEvery<GuildQuestion, GuildAnswer>((key, question) => new GuildAnswer($"every {key} {question.Detail}"));

        var guilds = await standIn.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("a", "b"), Timeout);
        var players = await standIn.GatherAsync<PlayerQuestion, GuildAnswer>(Players("a"), Timeout);

        guilds.Results["a"].Name.ShouldBe("guild a");
        guilds.Results["b"].Name.ShouldBe("every b detail-b");
        guilds.IsComplete.ShouldBeTrue();
        players.Results["a"].Name.ShouldBe("player a");
    }

    [Fact]
    public async Task GatherAsync_ShouldSettleAFailureThatIsTheRequestsFault_AndNotOneTheResponderGaveUpOn()
    {
        var standIn = new ScatterGatherClientStandIn(_time)
            .Fail<GuildQuestion>("missing", NotFound)
            .Fail<GuildQuestion>("unavailable", Down);

        var gathered = await standIn.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("missing", "unavailable"), Timeout);

        gathered.Failures.Keys.ShouldBe(["missing", "unavailable"], ignoreOrder: true);
        gathered.SettledKeys.ShouldBe(["missing"]);
        gathered["unavailable"].Errors.ShouldHaveSingleItem().Code.ShouldBe("SERVERUNAVAILABLE");
    }

    [Fact]
    public async Task GatherAsync_ShouldWaitForTheTimeout_UntilTheTestMovesTheClockPastIt_WhenAnItemNeverAnswers()
    {
        var standIn = new ScatterGatherClientStandIn(_time)
            .Answer<GuildQuestion, GuildAnswer>("a", new GuildAnswer("guild a"))
            .NeverAnswer<GuildQuestion>("silent");

        var gathering = standIn.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("a", "silent", "unscripted"), Timeout);
        await standIn.TimeoutWaitsReachedAsync(1);
        _time.Advance(Timeout - TimeSpan.FromTicks(1));
        gathering.IsCompleted.ShouldBeFalse();
        _time.Advance(TimeSpan.FromTicks(1));
        var gathered = await gathering;

        gathered.Results.Keys.ShouldBe(["a"]);
        gathered.UnansweredKeys.ShouldBe(["silent", "unscripted"]);
        gathered["silent"].Errors.ShouldHaveSingleItem().Type.ShouldBe(ErrorType.Timeout);
    }

    [Fact]
    public async Task GatherAsync_ShouldEndAtOnce_WhenOnCallMovedTheClockPastTheTimeout()
    {
        var standIn = new ScatterGatherClientStandIn(_time) { OnCall = call => _time.Advance(call.Timeout) };

        var gathered = await standIn.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("silent"), Timeout);

        gathered.UnansweredKeys.ShouldBe(["silent"]);
        standIn.TimeoutWaits.ShouldBe(1);
    }

    [Fact]
    public async Task NeverAnswerAny_ShouldSilenceEveryItemOfTheType_UntilResumeAnswering()
    {
        var standIn = new ScatterGatherClientStandIn(_time) { OnCall = call => _time.Advance(call.Timeout) }
            .AnswerEvery<GuildQuestion, GuildAnswer>((key, _) => new GuildAnswer(key))
            .NeverAnswerAny<GuildQuestion>();

        var down = await standIn.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("a"), Timeout);
        standIn.ResumeAnswering<GuildQuestion>();
        var up = await standIn.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("a"), Timeout);

        down.UnansweredKeys.ShouldBe(["a"]);
        up.Results.Keys.ShouldBe(["a"]);
    }

    [Fact]
    public async Task ThrowOnNextCall_ShouldThrowOnce_AndThrowOnCallOnTheCallNumbered()
    {
        var first = new InvalidOperationException("The bus failed.");
        var third = new InvalidOperationException("The bus failed again.");
        var standIn = new ScatterGatherClientStandIn(_time)
            .AnswerEvery<GuildQuestion, GuildAnswer>((key, _) => new GuildAnswer(key))
            .ThrowOnNextCall(first)
            .ThrowOnCall(2, third);

        (await Should.ThrowAsync<InvalidOperationException>(() => standIn.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("a"), Timeout)))
            .ShouldBeSameAs(first);
        (await standIn.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("a"), Timeout)).IsComplete.ShouldBeTrue();
        (await Should.ThrowAsync<InvalidOperationException>(() => standIn.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("a"), Timeout)))
            .ShouldBeSameAs(third);
        standIn.Calls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Hold_ShouldKeepEveryCallWaiting_UntilRelease()
    {
        var standIn = new ScatterGatherClientStandIn(_time)
            .AnswerEvery<GuildQuestion, GuildAnswer>((key, _) => new GuildAnswer(key));
        standIn.Hold();

        var gathering = standIn.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("a"), Timeout);
        await standIn.WaitUntilHeldAsync();

        gathering.IsCompleted.ShouldBeFalse();
        standIn.Calls.ShouldHaveSingleItem().Keys.ShouldBe(["a"]);
        standIn.Release();
        (await gathering).Results.Keys.ShouldBe(["a"]);
        (await standIn.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("b"), Timeout)).Results.Keys.ShouldBe(["b"]);
    }

    [Fact]
    public async Task Calls_ShouldRecordWhatWasAsked_InOrder()
    {
        var standIn = new ScatterGatherClientStandIn(_time)
            .AnswerEvery<GuildQuestion, GuildAnswer>((key, _) => new GuildAnswer(key))
            .AnswerEvery<PlayerQuestion, GuildAnswer>((key, _) => new GuildAnswer(key));

        await standIn.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("a", "b"), Timeout);
        await standIn.GatherAsync<PlayerQuestion, GuildAnswer>(Players("p"), TimeSpan.FromSeconds(5));

        standIn.Calls.Select(call => call.RequestType).ShouldBe([typeof(GuildQuestion), typeof(PlayerQuestion)]);
        var guilds = standIn.CallsOf<GuildQuestion>().ShouldHaveSingleItem();
        guilds.Kind.ShouldBe(ScatterGatherCallKind.Gather);
        guilds.Keys.ShouldBe(["a", "b"]);
        guilds.Timeout.ShouldBe(Timeout);
        guilds.Requests["b"].ShouldBe(new GuildQuestion("detail-b"));
        standIn.CallsOf<PlayerQuestion>().ShouldHaveSingleItem().Timeout.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RequestAsync_ShouldAnswerTheItemKeyedRequest()
    {
        var standIn = new ScatterGatherClientStandIn(_time)
            .Answer<GuildQuestion, GuildAnswer>(ScatterGatherClientStandIn.RequestKey, new GuildAnswer("one"));

        var answer = await standIn.RequestAsync<GuildQuestion, GuildAnswer>(new GuildQuestion("x"), Timeout);

        answer.Value.Name.ShouldBe("one");
        standIn.Calls.ShouldHaveSingleItem().Kind.ShouldBe(ScatterGatherCallKind.Request);
    }

    [Fact]
    public async Task StreamAsync_ShouldYieldEveryItemThatAnswered()
    {
        var standIn = new ScatterGatherClientStandIn(_time) { OnCall = call => _time.Advance(call.Timeout) }
            .Answer<GuildQuestion, GuildAnswer>("a", new GuildAnswer("a"))
            .Fail<GuildQuestion>("b", NotFound);

        var replies = new List<IScatterReply<GuildAnswer>>();
        await foreach (var reply in standIn.StreamAsync<GuildQuestion, GuildAnswer>(Guilds("a", "b", "silent"), Timeout))
            replies.Add(reply);

        replies.Select(reply => reply.Key).ShouldBe(["a", "b"]);
        replies[1].Result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task GatherInChunksAsync_ShouldScriptRecordAndThrowEachChunkAsAGather_OnTheTestsClock()
    {
        // Each chunk takes its 4 s timeout on the test's clock; the fourth chunk no longer fits in the 13 s budget.
        var standIn = new ScatterGatherClientStandIn(_time) { OnCall = call => _time.Advance(call.Timeout) }
            .AnswerEvery<GuildQuestion, GuildAnswer>((key, _) => new GuildAnswer(key))
            .ThrowOnCall(1, new InvalidOperationException("The bus failed on this chunk."));

        var chunks = new List<GatheredChunk<GuildAnswer>>();
        await foreach (var chunk in standIn.GatherInChunksAsync<GuildQuestion, GuildAnswer>(
            Guilds("a", "b", "c", "d", "e"),
            1,
            _ => TimeSpan.FromSeconds(4),
            budget: TimeSpan.FromSeconds(13)))
            chunks.Add(chunk);

        chunks.Select(chunk => chunk.Status).ShouldBe(
            [GatheredChunkStatus.Gathered, GatheredChunkStatus.Failed, GatheredChunkStatus.Gathered, GatheredChunkStatus.NotAsked]);
        chunks[3].Keys.ShouldBe(["d", "e"]);
        standIn.Calls.Select(call => string.Join(",", call.Keys)).ShouldBe(["a", "b", "c"]);
        standIn.Calls.ShouldAllBe(call => call.Timeout == TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task HoldScatterGathers_ShouldForwardEveryCallToTheHostsClient_WhileRecordingAndHoldingIt()
    {
        var inner = new FakeScatterGatherTransport(key => new GuildAnswer($"real {key}"));
        var standIn = new ScatterGatherClientStandIn(_time);
        var services = new ServiceCollection();
        services.AddSingleton<IScatterGatherClient>(inner);
        services.HoldScatterGathers(standIn);
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IScatterGatherClient>();
        standIn.Hold();

        var gathering = client.GatherAsync<GuildQuestion, GuildAnswer>(Guilds("a"), Timeout);
        await standIn.WaitUntilHeldAsync();
        inner.Sent.ShouldBeEmpty();
        standIn.Release();
        var gathered = await gathering;

        client.ShouldBeSameAs(standIn);
        gathered.Results["a"].Name.ShouldBe("real a");
        standIn.Calls.ShouldHaveSingleItem().Keys.ShouldBe(["a"]);
    }

    [Fact]
    public void ReplaceScatterGatherClient_ShouldMakeTheStandInTheOnlyClient()
    {
        var standIn = new ScatterGatherClientStandIn(_time);
        var services = new ServiceCollection();
        services.AddSingleton<IScatterGatherClient>(new FakeScatterGatherTransport(key => key));

        services.ReplaceScatterGatherClient(standIn);

        services.Where(descriptor => descriptor.ServiceType == typeof(IScatterGatherClient))
            .ShouldHaveSingleItem().ImplementationInstance.ShouldBeSameAs(standIn);
    }

    [Fact]
    public async Task GatherAsync_ShouldThrow_WhenAnItemIsScriptedWithAnotherResponseType()
    {
        var standIn = new ScatterGatherClientStandIn(_time)
            .Answer<GuildQuestion, GuildAnswer>("a", new GuildAnswer("a"));

        (await Should.ThrowAsync<InvalidOperationException>(() => standIn.GatherAsync<GuildQuestion, OtherAnswer>(Guilds("a"), Timeout)))
            .Message.ShouldContain("GuildAnswer");
    }

    [Fact]
    public async Task GatherAsync_ShouldRefuseATimeoutThatIsntPositive()
        => await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => new ScatterGatherClientStandIn(_time).GatherAsync<GuildQuestion, GuildAnswer>(Guilds("a"), TimeSpan.Zero));

    private static Dictionary<string, GuildQuestion> Guilds(params string[] keys)
        => keys.ToDictionary(key => key, key => new GuildQuestion($"detail-{key}"), StringComparer.Ordinal);

    private static Dictionary<string, PlayerQuestion> Players(params string[] keys)
        => keys.ToDictionary(key => key, key => new PlayerQuestion(key), StringComparer.Ordinal);

    internal sealed record GuildQuestion(string Detail);

    internal sealed record PlayerQuestion(string Id);

    internal sealed record GuildAnswer(string Name);

    internal sealed record OtherAnswer(string Name);
}
