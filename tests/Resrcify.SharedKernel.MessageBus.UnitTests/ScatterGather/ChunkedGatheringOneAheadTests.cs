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
/// The chunked gather asks each next chunk as soon as the one before is gathered, while the caller handles that one, and
/// never more than one chunk ahead. The client's gathers end when the test lets them (<see cref="GatedClient"/>), and the
/// caller holds each chunk until the test lets it go, so every step happens where the test puts it, on a fake clock:
/// nothing waits on time. What happened is read from one ordered record of the client's and the caller's steps.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ChunkedGatheringOneAheadTests
{
    /// <summary>A bound for waits that only a failed test reaches.</summary>
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _time = new();
    private readonly Steps _steps = new();

    [Fact]
    public async Task GatherAsync_ShouldGatherTheNextChunkWhileTheCallerHoldsOne_AndNoChunkFurtherAhead()
    {
        var client = new GatedClient(_steps);
        var caller = new HoldingCaller(_steps, chunks: 4);
        var consuming = caller.ConsumeAsync(Gather(client, Requests("a", "b", "c", "d")));

        await client.StartedAsync(1);
        client.StartedCount.ShouldBe(1);   // the first chunk only, until it is gathered
        client.Release(0);
        await caller.HoldingAsync(0);

        client.StartedCount.ShouldBe(2, "the next chunk wasn't asked while the caller held the first");
        client.Release(1);
        client.StartedCount.ShouldBe(2, "a chunk two ahead of the caller was asked");

        caller.Let(0);
        await caller.HoldingAsync(1);
        client.StartedCount.ShouldBe(3);
        client.Release(2);
        caller.Let(1);
        await caller.HoldingAsync(2);
        client.StartedCount.ShouldBe(4);
        client.Release(3);
        caller.Let(2);
        await caller.HoldingAsync(3);
        caller.Let(3);
        await consuming.WaitAsync(GiveUpAfter);

        _steps.All.ShouldBe([
            "start 0", "end 0",
            "start 1", "got 0", "end 1", "done 0",
            "start 2", "got 1", "end 2", "done 1",
            "start 3", "got 2", "end 3", "done 2",
            "got 3", "done 3"]);
        client.MostInFlight.ShouldBe(1);
        caller.Got.ShouldBe([0, 1, 2, 3]);
    }

    [Fact]
    public async Task GatherAsync_ShouldYieldEveryChunkOnceInOrder_WhenAChunkAheadThrows()
    {
        var failure = new InvalidOperationException("The broker went away.");
        var client = new GatedClient(_steps);
        var caller = new HoldingCaller(_steps, chunks: 4);
        var consuming = caller.ConsumeAsync(Gather(client, Requests("a", "b", "c", "d")));

        await client.StartedAsync(1);
        client.Release(0);
        await caller.HoldingAsync(0);
        client.Fail(1, failure);   // thrown while the caller holds chunk 0
        caller.Let(0);
        await caller.HoldingAsync(1);
        client.Release(2);
        caller.Let(1);
        await caller.HoldingAsync(2);
        client.Release(3);
        caller.Let(2);
        await caller.HoldingAsync(3);
        caller.Let(3);
        await consuming.WaitAsync(GiveUpAfter);

        caller.Got.ShouldBe([0, 1, 2, 3]);
        caller.Chunks.Select(chunk => chunk.Status)
            .ShouldBe([GatheredChunkStatus.Gathered, GatheredChunkStatus.Failed, GatheredChunkStatus.Gathered, GatheredChunkStatus.Gathered]);
        caller.Chunks[1].Exception.ShouldBeSameAs(failure);
        client.StartedKeys.ShouldBe(["a", "b", "c", "d"]);
        client.MostInFlight.ShouldBe(1);
    }

    [Fact]
    public async Task GatherAsync_ShouldCheckTheBudgetAsAChunkIsAboutToStart_AndNotAskItNorAnyAfterIt()
    {
        // Each chunk's timeout is 4 s, the budget 10 s. The second chunk starts at 0 s, as soon as the first is
        // gathered, and is gathered at once; the caller then works 7 s on the first. The third would start at 7 s and
        // end at 11 s: not asked, nor anything after it. (Checked when the second chunk was gathered, at 0 s, it would
        // have fitted.)
        var client = new GatedClient(_steps);
        var caller = new HoldingCaller(_steps, chunks: 3);
        var consuming = caller.ConsumeAsync(Gather(
            client,
            Requests("a", "b", "c", "d", "e"),
            timeoutForChunk: _ => Seconds(4),
            budget: Seconds(10)));

        await client.StartedAsync(1);
        client.Release(0);
        await caller.HoldingAsync(0);
        client.Release(1);
        _time.Advance(Seconds(7));   // the caller's work on the first chunk
        caller.Let(0);
        await caller.HoldingAsync(1);
        caller.Let(1);
        await caller.HoldingAsync(2);
        caller.Let(2);
        await consuming.WaitAsync(GiveUpAfter);

        caller.Chunks.Select(chunk => chunk.Status)
            .ShouldBe([GatheredChunkStatus.Gathered, GatheredChunkStatus.Gathered, GatheredChunkStatus.NotAsked]);
        caller.Chunks[2].Keys.ShouldBe(["c", "d", "e"]);
        caller.Chunks[2].Index.ShouldBe(2);
        client.StartedCount.ShouldBe(2);
    }

    [Fact]
    public async Task DisposeAsync_ShouldCancelTheChunkGatheredAhead_AndReturnOnlyOnceItEnded_WhenTheCallerStopsEarly()
    {
        var client = new GatedClient(_steps);
        var enumerator = Gather(client, Requests("a", "b", "c")).GetAsyncEnumerator();
        var first = NextAsync(enumerator);
        await client.StartedAsync(1);
        client.Release(0);
        (await first.WaitAsync(GiveUpAfter)).ShouldBeTrue();

        // The caller stops with the first chunk (a break), while the second is being gathered.
        var disposing = enumerator.DisposeAsync().AsTask();
        await client.CancellationSeenAsync(1);
        disposing.IsCompleted.ShouldBeFalse("DisposeAsync returned while the chunk ahead was still being gathered");

        client.Release(1);   // it ends now (as cancelled)
        await disposing.WaitAsync(GiveUpAfter);

        client.StartedCount.ShouldBe(2, "a chunk was asked after the caller stopped");
        _steps.All.ShouldBe(["start 0", "end 0", "start 1", "cancelled 1", "end 1"]);
    }

    [Fact]
    public async Task MoveNextAsync_ShouldThrow_AfterTheChunkGatheredAheadEnded_WhenTheCallerCancelsWhileHoldingAChunk()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new GatedClient(_steps);
        var enumerator = Gather(client, Requests("a", "b", "c"), cancellationToken: cancellation.Token).GetAsyncEnumerator();
        try
        {
            var first = NextAsync(enumerator);
            await client.StartedAsync(1);
            client.Release(0);
            (await first.WaitAsync(GiveUpAfter)).ShouldBeTrue();

            await cancellation.CancelAsync();   // while the caller holds the first chunk
            await client.CancellationSeenAsync(1);
            var next = NextAsync(enumerator);
            next.IsCompleted.ShouldBeFalse("the gather stopped before the chunk ahead had ended");
            client.Release(1);

            await Should.ThrowAsync<OperationCanceledException>(next.WaitAsync(GiveUpAfter));
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        client.StartedCount.ShouldBe(2, "a chunk was asked after the caller cancelled");
        _steps.All.ShouldBe(["start 0", "end 0", "start 1", "cancelled 1", "end 1"]);
    }

    private IAsyncEnumerable<GatheredChunk<Answer>> Gather(
        GatedClient client,
        Dictionary<string, Question> requests,
        Func<int, TimeSpan>? timeoutForChunk = null,
        TimeSpan? budget = null,
        CancellationToken cancellationToken = default)
        => ChunkedGathering.GatherAsync<Question, Answer>(
            client,
            requests,
            chunkSize: 1,
            timeoutForChunk ?? (_ => Seconds(1)),
            budget,
            _time,
            cancellationToken);

    // The enumerator's next chunk, as a task the test can look at while it is pending.
    private static async Task<bool> NextAsync(IAsyncEnumerator<GatheredChunk<Answer>> enumerator)
        => await enumerator.MoveNextAsync();
    private static Dictionary<string, Question> Requests(params string[] keys)
        => keys.ToDictionary(key => key, key => new Question(key), StringComparer.Ordinal);

    private static TimeSpan Seconds(int seconds)
        => TimeSpan.FromSeconds(seconds);

    internal sealed record Question(string Key);

    internal sealed record Answer(string Key);

    /// <summary>What the client and the caller did, in the order they did it.</summary>
    private sealed class Steps
    {
        private readonly Lock _gate = new();
        private readonly List<string> _steps = [];

        public IReadOnlyList<string> All
        {
            get
            {
                lock (_gate)
                    return [.. _steps];
            }
        }

        public void Add(string step)
        {
            lock (_gate)
                _steps.Add(step);
        }
    }

    /// <summary>
    /// Enumerates the chunks as a caller that stores each one does: it takes a chunk ("got"), holds it until the test
    /// lets it go (<see cref="Let"/>), then asks for the next ("done").
    /// </summary>
    private sealed class HoldingCaller(
        Steps steps,
        int chunks)
    {
        private readonly TaskCompletionSource[] _holding = Signals(chunks);
        private readonly TaskCompletionSource[] _let = Signals(chunks);

        public List<int> Got { get; } = [];

        public List<GatheredChunk<Answer>> Chunks { get; } = [];

        public Task ConsumeAsync(IAsyncEnumerable<GatheredChunk<Answer>> gathering)
            => Task.Run(async () =>
            {
                await foreach (var chunk in gathering)
                {
                    steps.Add($"got {chunk.Index}");
                    Got.Add(chunk.Index);
                    Chunks.Add(chunk);
                    _holding[chunk.Index].TrySetResult();
                    await _let[chunk.Index].Task.WaitAsync(GiveUpAfter);
                    steps.Add($"done {chunk.Index}");
                }
            });

        /// <summary>Completes once the caller holds chunk <paramref name="index"/>.</summary>
        public Task HoldingAsync(int index)
            => _holding[index].Task.WaitAsync(GiveUpAfter);

        public void Let(int index)
            => _let[index].TrySetResult();

        private static TaskCompletionSource[] Signals(int count)
            => [.. Enumerable.Range(0, count).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))];
    }

    /// <summary>
    /// A client whose gathers end when the test says: answered (<see cref="Release"/>) or thrown (<see cref="Fail"/>).
    /// A gather whose token is cancelled records it and still ends only when released, then throws as cancelled. It
    /// records each gather's start and end, and the most gathers under way at once.
    /// </summary>
    private sealed class GatedClient(Steps steps) : IScatterGatherClient
    {
        private readonly Lock _gate = new();
        private readonly List<Call> _calls = [];
        private readonly List<(int Count, TaskCompletionSource Reached)> _startedSignals = [];
        private int _inFlight;
        private int _mostInFlight;

        public int StartedCount
        {
            get
            {
                lock (_gate)
                    return _calls.Count;
            }
        }

        public IReadOnlyList<string> StartedKeys
        {
            get
            {
                lock (_gate)
                    return [.. _calls.SelectMany(call => call.Keys)];
            }
        }

        public int MostInFlight
        {
            get
            {
                lock (_gate)
                    return _mostInFlight;
            }
        }

        /// <summary>Completes once <paramref name="count"/> gathers have started.</summary>
        public Task StartedAsync(int count)
        {
            lock (_gate)
            {
                if (_calls.Count >= count)
                    return Task.CompletedTask;

                var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _startedSignals.Add((count, reached));
                return reached.Task.WaitAsync(GiveUpAfter);
            }
        }

        /// <summary>Completes once gather <paramref name="index"/> has seen its token cancelled.</summary>
        public Task CancellationSeenAsync(int index)
            => CallAt(index).CancellationSeen.Task.WaitAsync(GiveUpAfter);

        /// <summary>Gather <paramref name="index"/> ends: every item answered (or, cancelled, it throws as such).</summary>
        public void Release(int index)
            => CallAt(index).Ended.TrySetResult(null);

        /// <summary>Gather <paramref name="index"/> ends throwing <paramref name="failure"/>.</summary>
        public void Fail(
            int index,
            Exception failure)
            => CallAt(index).Ended.TrySetResult(failure);

        public async Task<IGathered<TResponse>> GatherAsync<TRequest, TResponse>(
            IReadOnlyDictionary<string, TRequest> requests,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            where TRequest : class
            where TResponse : class
        {
            var call = Arrive(requests.Keys);
            await using var seen = cancellationToken.Register(() =>
            {
                steps.Add($"cancelled {call.Index}");
                call.CancellationSeen.TrySetResult();
            });
            try
            {
                var failure = await call.Ended.Task;
                cancellationToken.ThrowIfCancellationRequested();
                if (failure is not null)
                    throw failure;

                return new Gathered<TResponse>(
                    call.Keys,
                    call.Keys.ToDictionary(key => key, key => (TResponse)(object)new Answer(key), StringComparer.Ordinal),
                    new Dictionary<string, IReadOnlyList<Error>>(StringComparer.Ordinal));
            }
            finally
            {
                lock (_gate)
                    _inFlight--;
                steps.Add($"end {call.Index}");
            }
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

        private Call Arrive(IEnumerable<string> keys)
        {
            List<TaskCompletionSource> reached;
            Call call;
            lock (_gate)
            {
                call = new Call(_calls.Count, [.. keys]);
                _calls.Add(call);
                _inFlight++;
                _mostInFlight = Math.Max(_mostInFlight, _inFlight);
                steps.Add($"start {call.Index}");
                reached = [.. _startedSignals.Where(signal => signal.Count <= _calls.Count).Select(signal => signal.Reached)];
                _startedSignals.RemoveAll(signal => signal.Count <= _calls.Count);
            }

            foreach (var signal in reached)
                signal.TrySetResult();
            return call;
        }

        private Call CallAt(int index)
        {
            lock (_gate)
                return _calls[index];
        }

        /// <summary>
        /// One gather. <see cref="Ended"/> completes inline: the gather (and the chunked gather awaiting it) goes on on
        /// the test's thread, before <see cref="Release"/> returns, so what the test checks next has happened already.
        /// </summary>
        private sealed record Call(
            int Index,
            IReadOnlyList<string> Keys)
        {
            public TaskCompletionSource<Exception?> Ended { get; } = new();

            public TaskCompletionSource CancellationSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
