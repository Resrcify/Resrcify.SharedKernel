using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.ScatterGather;

/// <summary>
/// The chunked gather on a scripted client: each chunk's gather is answered (or thrown) by the script, which can move a
/// fake clock as a slow chunk would. Deterministic: nothing waits on real time.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ChunkedGatheringTests
{
    private static readonly Error NotFound = Error.NotFound("RECORDNOTFOUND", "No such guild.");

    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task GatherAsync_ShouldYieldTheChunksInTheRequestsOrder_EachWithItsKeysAndTimeout()
    {
        var client = new ScriptedClient();

        var chunks = await ToListAsync(Gather(client, Requests("a", "b", "c", "d", "e"), chunkSize: 2));

        chunks.Select(chunk => chunk.Index).ShouldBe([0, 1, 2]);
        chunks.ShouldAllBe(chunk => chunk.Count == 3 && chunk.IsGathered);
        chunks.Select(chunk => string.Join(",", chunk.Keys)).ShouldBe(["a,b", "c,d", "e"]);
        chunks.Select(chunk => chunk.Timeout).ShouldBe([Seconds(2), Seconds(2), Seconds(1)]);
        client.Calls.Select(call => string.Join(",", call.Keys)).ShouldBe(["a,b", "c,d", "e"]);
        client.Calls.Select(call => call.Timeout).ShouldBe([Seconds(2), Seconds(2), Seconds(1)]);
        chunks[1].Gathered!.Results.Keys.ShouldBe(["c", "d"], ignoreOrder: true);
    }

    [Fact]
    public async Task GatherAsync_ShouldAskTheFirstChunkWhenTheCallerAsksForIt_AndEachNextOneAheadOfTheCaller()
    {
        var client = new ScriptedClient();
        var chunks = Gather(client, Requests("a", "b", "c", "d"), chunkSize: 1);

        client.Calls.ShouldBeEmpty();
        await using var enumerator = chunks.GetAsyncEnumerator();
        client.Calls.ShouldBeEmpty();

        // The caller holds a chunk while the next one is asked; the one after that waits for the caller to take it.
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        client.Calls.Count.ShouldBe(2);
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        client.Calls.Count.ShouldBe(3);
        enumerator.Current.Keys.ShouldBe(["b"]);
    }

    [Fact]
    public async Task GatherAsync_ShouldYieldAThrowingChunkAsFailed_AndStillAskTheNext()
    {
        var failure = new InvalidOperationException("The broker went away.");
        var client = new ScriptedClient { ThrowOnCall = { [1] = failure } };

        var chunks = await ToListAsync(Gather(client, Requests("a", "b", "c"), chunkSize: 1));

        chunks.Select(chunk => chunk.Status)
            .ShouldBe([GatheredChunkStatus.Gathered, GatheredChunkStatus.Failed, GatheredChunkStatus.Gathered]);
        chunks[1].IsFailed.ShouldBeTrue();
        chunks[1].Exception.ShouldBeSameAs(failure);
        chunks[1].Gathered.ShouldBeNull();
        chunks[1].Counts.ShouldBeSameAs(GatheredCounts.None);
        client.Calls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GatherAsync_ShouldReportTheChunksThatNoLongerFitInTheBudgetOnce_WithTheirKeys_AndAskNoneOfThem()
    {
        // Every chunk takes its whole 4 s timeout: the third would end at 12 s, past the 10 s budget.
        var client = new ScriptedClient { OnCall = _ => _time.Advance(Seconds(4)) };

        var chunks = await ToListAsync(Gather(
            client,
            Requests("a", "b", "c", "d", "e"),
            chunkSize: 1,
            timeoutForChunk: _ => Seconds(4),
            budget: Seconds(10)));

        chunks.Count.ShouldBe(3);
        chunks.Take(2).ShouldAllBe(chunk => chunk.IsGathered);
        var notAsked = chunks[2];
        notAsked.IsNotAsked.ShouldBeTrue();
        notAsked.Index.ShouldBe(2);
        notAsked.Count.ShouldBe(5);
        notAsked.Keys.ShouldBe(["c", "d", "e"]);
        notAsked.Timeout.ShouldBe(Seconds(4));
        notAsked.Counts.ShouldBeSameAs(GatheredCounts.None);
        client.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GatherAsync_ShouldCountTheCallersWorkBetweenChunks_AgainstTheBudget()
    {
        var client = new ScriptedClient();
        var chunks = new List<GatheredChunk<Answer>>();

        await foreach (var chunk in Gather(client, Requests("a", "b", "c"), chunkSize: 1, timeoutForChunk: _ => Seconds(2), budget: Seconds(5)))
        {
            chunks.Add(chunk);
            _time.Advance(Seconds(4));   // the caller stores the chunk
        }

        // The second chunk was asked at 0 s, ahead of the caller's work on the first; the third would start at 4 s,
        // once the caller took the second, and end at 6 s, past the 5 s budget.
        chunks.Select(chunk => chunk.Status)
            .ShouldBe([GatheredChunkStatus.Gathered, GatheredChunkStatus.Gathered, GatheredChunkStatus.NotAsked]);
        client.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GatherAsync_ShouldGiveCountsThatSumToTheWholeGathers()
    {
        var client = new ScriptedClient { NotFoundKeys = { "b", "e" } };

        var chunks = await ToListAsync(Gather(client, Requests("a", "b", "c", "d", "e"), chunkSize: 2));
        var total = chunks.Aggregate(GatheredCounts.None, (sum, chunk) => sum + chunk.Counts);

        total.Asked.ShouldBe(5);
        total.Answered.ShouldBe(3);
        total.Failed.ShouldBe(2);
        total.FailedCodes.ShouldBe(" (RECORDNOTFOUND 2)");
        chunks.Select(chunk => chunk.Counts.Answered).ShouldBe([1, 2, 0]);
    }

    [Fact]
    public async Task GatherAsync_ShouldYieldNothingAndAskNothing_WhenThereAreNoRequests()
    {
        var client = new ScriptedClient();

        (await ToListAsync(Gather(client, Requests(), chunkSize: 10))).ShouldBeEmpty();
        client.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GatherAsync_ShouldRefuseAChunkSizeBelowOne_WhenCalled(int chunkSize)
        => Should.Throw<ArgumentOutOfRangeException>(() => Gather(new ScriptedClient(), Requests("a"), chunkSize))
            .ParamName.ShouldBe("chunkSize");

    [Fact]
    public void GatherAsync_ShouldRefuseANegativeBudget_WhenCalled()
        => Should.Throw<ArgumentOutOfRangeException>(() => Gather(new ScriptedClient(), Requests("a"), 1, budget: TimeSpan.FromTicks(-1)))
            .ParamName.ShouldBe("budget");

    [Fact]
    public async Task GatherAsync_ShouldThrow_WhenTheCallerCancelsDuringAChunk()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new ScriptedClient { OnCall = call => { if (call == 1) cancellation.Cancel(); } };

        var chunks = new List<GatheredChunk<Answer>>();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chunk in Gather(client, Requests("a", "b", "c"), chunkSize: 1, cancellationToken: cancellation.Token))
                chunks.Add(chunk);
        });

        chunks.Count.ShouldBe(1);
        client.Calls.Count.ShouldBe(2);
    }

    private IAsyncEnumerable<GatheredChunk<Answer>> Gather(
        ScriptedClient client,
        Dictionary<string, Question> requests,
        int chunkSize,
        Func<int, TimeSpan>? timeoutForChunk = null,
        TimeSpan? budget = null,
        CancellationToken cancellationToken = default)
        => ChunkedGathering.GatherAsync<Question, Answer>(
            client,
            requests,
            chunkSize,
            timeoutForChunk ?? (count => Seconds(count)),
            budget,
            _time,
            cancellationToken);

    private static async Task<List<GatheredChunk<Answer>>> ToListAsync(IAsyncEnumerable<GatheredChunk<Answer>> chunks)
    {
        var list = new List<GatheredChunk<Answer>>();
        await foreach (var chunk in chunks)
            list.Add(chunk);
        return list;
    }

    private static Dictionary<string, Question> Requests(params string[] keys)
        => keys.ToDictionary(key => key, key => new Question(key), StringComparer.Ordinal);

    private static TimeSpan Seconds(int seconds)
        => TimeSpan.FromSeconds(seconds);

    internal sealed record Question(string Key);

    internal sealed record Answer(string Key);

    /// <summary>
    /// Answers each gather from its script: every item answered, but those in <see cref="NotFoundKeys"/> failed "not
    /// found"; the call numbered in <see cref="ThrowOnCall"/> throws; <see cref="OnCall"/> runs first (moves the clock,
    /// cancels).
    /// </summary>
    private sealed class ScriptedClient : IScatterGatherClient
    {
        public List<(IReadOnlyList<string> Keys, TimeSpan Timeout)> Calls { get; } = [];

        public Dictionary<int, Exception> ThrowOnCall { get; } = [];

        public HashSet<string> NotFoundKeys { get; } = new(StringComparer.Ordinal);

        public Action<int>? OnCall { get; init; }

        public Task<IGathered<TResponse>> GatherAsync<TRequest, TResponse>(
            IReadOnlyDictionary<string, TRequest> requests,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class
        {
            var call = Calls.Count;
            Calls.Add(([.. requests.Keys], timeout));
            OnCall?.Invoke(call);
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnCall.TryGetValue(call, out var failure))
                throw failure;

            var gathered = new Gathered(
                requests.Keys.Where(key => !NotFoundKeys.Contains(key)).ToDictionary(key => key, key => new Answer(key), StringComparer.Ordinal),
                requests.Keys.Where(NotFoundKeys.Contains).ToDictionary(key => key, _ => (IReadOnlyList<Error>)[NotFound], StringComparer.Ordinal));
            return Task.FromResult((IGathered<TResponse>)(object)gathered);
        }

        public Task<Result<TResponse>> RequestAsync<TRequest, TResponse>(
            TRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class
            => throw new NotSupportedException();

        public IAsyncEnumerable<IScatterReply<TResponse>> StreamAsync<TRequest, TResponse>(
            IReadOnlyDictionary<string, TRequest> requests,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class
            => throw new NotSupportedException();

        // The tests call ChunkedGathering directly, with their clock.
        public IAsyncEnumerable<GatheredChunk<TResponse>> GatherInChunksAsync<TRequest, TResponse>(
            IReadOnlyDictionary<string, TRequest> requests,
            int chunkSize,
            Func<int, TimeSpan> timeoutForChunk,
            TimeSpan? budget = null,
            CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class
            => throw new NotSupportedException();
    }

    private sealed class Gathered(
        IReadOnlyDictionary<string, Answer> results,
        IReadOnlyDictionary<string, IReadOnlyList<Error>> failures)
        : IGathered<Answer>
    {
        public IReadOnlyDictionary<string, Answer> Results
            => results;

        public IReadOnlyDictionary<string, IReadOnlyList<Error>> Failures
            => failures;

        public IReadOnlyList<string> UnansweredKeys
            => [];

        public IReadOnlyCollection<string> SettledKeys
            => [.. results.Keys.Concat(failures.Keys)];

        public bool IsComplete
            => true;

        public Result<Answer> this[string key]
            => results.TryGetValue(key, out var answer) ? answer : Result.Failure<Answer>(NotFound);
    }
}
