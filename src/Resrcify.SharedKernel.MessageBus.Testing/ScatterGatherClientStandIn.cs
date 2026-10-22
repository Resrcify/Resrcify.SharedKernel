using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.Testing;

/// <summary>
/// An <see cref="IScatterGatherClient"/> for tests, scripted item by item, on the test's clock: what each item of a
/// request type answers (a response, a responder's errors, or nothing), which call throws, which calls are held until
/// the test lets them go, and a record of every call (keys, timeout, requests).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>An item answers as scripted for its request type and key (<see cref="Answer{TRequest, TResponse}"/>,
/// <see cref="Fail{TRequest}"/>, <see cref="NeverAnswer{TRequest}"/>), else as its request type answers every item
/// (<see cref="AnswerEvery{TRequest, TResponse}"/>), else not at all. <see cref="NeverAnswerAny{TRequest}"/> (an outage)
/// silences every item of the type until <see cref="ResumeAnswering{TRequest}"/>.</item>
/// <item>A failure is settled (the request's fault) unless one of its errors is transient
/// (<c>ErrorTypeExtensions.IsTransient</c>), as the message bus decides: <c>NotFound</c> is settled,
/// <c>ExternalFailure</c> isn't.</item>
/// <item>A call with an item that never answers waits for its timeout on the clock given: it ends when the test moves
/// the clock past it (or has moved it already, e.g. from <see cref="OnCall"/>). Every other call answers at once.</item>
/// <item><see cref="GatherInChunksAsync"/> runs the message bus' own chunking on <see cref="GatherAsync"/> and the
/// test's clock: each chunk is a gather of its own here (scripted, recorded, held).</item>
/// <item>Put in front of the host's real client (<c>HoldScatterGathers</c>), it forwards every call to it instead of
/// answering, still recording, holding and throwing as told.</item>
/// </list>
/// </remarks>
public sealed class ScatterGatherClientStandIn : IScatterGatherClient
{
    /// <summary>The key a <c>RequestAsync</c> item has, as the message bus' client gives it.</summary>
    public const string RequestKey = "request";

    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<(Type Request, string Key), object> _byKey = [];
    private readonly Dictionary<Type, Func<string, object, object>> _everyItem = [];
    private readonly HashSet<Type> _silenced = [];
    private readonly Dictionary<int, Exception> _throwOnCall = [];
    private readonly Queue<Exception> _throwOnNextCalls = new();
    private readonly List<ScatterGatherCall> _calls = [];
    private readonly List<(int Count, TaskCompletionSource Reached)> _waitingSignals = [];
    private TaskCompletionSource? _released;
    private TaskCompletionSource _held = NewSignal();
    private IScatterGatherClient? _forwardTo;
    private int _waiting;

    /// <param name="time">The clock a call that waits for its timeout, and the chunked gather's budget, run on.</param>
    public ScatterGatherClientStandIn(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>
    /// Runs as each call arrives, after it is recorded and before it is held or answered: to move a fake clock as a slow
    /// responder would (<c>call => time.Advance(call.Timeout)</c>), or to look at the call.
    /// </summary>
    public Action<ScatterGatherCall>? OnCall { get; set; }

    /// <summary>Every call so far, in the order they arrived.</summary>
    public IReadOnlyList<ScatterGatherCall> Calls
    {
        get
        {
            lock (_gate)
                return [.. _calls];
        }
    }

    /// <summary>How many calls have waited, or are waiting, for their timeout (they had an item that never answers).</summary>
    public int TimeoutWaits
        => Volatile.Read(ref _waiting);

    /// <summary>The calls for <typeparamref name="TRequest"/> so far, in order.</summary>
    public IReadOnlyList<ScatterGatherCall> CallsOf<TRequest>()
        => [.. Calls.Where(call => call.RequestType == typeof(TRequest))];

    /// <summary>The item <paramref name="key"/> of <typeparamref name="TRequest"/> answers with <paramref name="response"/>.</summary>
    public ScatterGatherClientStandIn Answer<TRequest, TResponse>(
        string key,
        TResponse response)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(response);
        return Script<TRequest>(key, response);
    }

    /// <summary>Every item of <typeparamref name="TRequest"/> not scripted by key answers with what <paramref name="answer"/> gives.</summary>
    public ScatterGatherClientStandIn AnswerEvery<TRequest, TResponse>(Func<string, TRequest, TResponse> answer)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(answer);
        lock (_gate)
            _everyItem[typeof(TRequest)] = (key, request) => answer(key, (TRequest)request);
        return this;
    }

    /// <summary>
    /// The item <paramref name="key"/> of <typeparamref name="TRequest"/> fails with <paramref name="errors"/>: settled
    /// unless one of them is transient.
    /// </summary>
    public ScatterGatherClientStandIn Fail<TRequest>(
        string key,
        params Error[] errors)
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Length == 0)
            throw new ArgumentException("A failure has at least one error.", nameof(errors));
        return Script<TRequest>(key, new ScriptedFailure(errors));
    }

    /// <summary>The item <paramref name="key"/> of <typeparamref name="TRequest"/> never answers (its call waits for its timeout).</summary>
    public ScatterGatherClientStandIn NeverAnswer<TRequest>(string key)
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(key);
        return Script<TRequest>(key, ScriptedSilence.Instance);
    }

    /// <summary>No item of <typeparamref name="TRequest"/> answers, whatever is scripted (an outage), until <see cref="ResumeAnswering{TRequest}"/>.</summary>
    public ScatterGatherClientStandIn NeverAnswerAny<TRequest>()
        where TRequest : class
    {
        lock (_gate)
            _silenced.Add(typeof(TRequest));
        return this;
    }

    /// <summary>The items of <typeparamref name="TRequest"/> answer as scripted again.</summary>
    public ScatterGatherClientStandIn ResumeAnswering<TRequest>()
        where TRequest : class
    {
        lock (_gate)
            _silenced.Remove(typeof(TRequest));
        return this;
    }

    /// <summary>The next call (of any kind; each chunk of a chunked gather is one) throws <paramref name="exception"/>, once.</summary>
    public ScatterGatherClientStandIn ThrowOnNextCall(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
            _throwOnNextCalls.Enqueue(exception);
        return this;
    }

    /// <summary>The call numbered <paramref name="call"/> (0 for the first, of any kind) throws <paramref name="exception"/>.</summary>
    public ScatterGatherClientStandIn ThrowOnCall(
        int call,
        Exception exception)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(call);
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
            _throwOnCall[call] = exception;
        return this;
    }

    /// <summary>
    /// Holds every call that arrives from now on, before it is answered (its timeout hasn't started), until
    /// <see cref="Release"/>.
    /// </summary>
    public void Hold()
    {
        lock (_gate)
        {
            _released ??= NewSignal();
            _held = NewSignal();
        }
    }

    /// <summary>Completes once a call is held: it arrived and waits for <see cref="Release"/>.</summary>
    public Task WaitUntilHeldAsync()
    {
        lock (_gate)
            return _held.Task;
    }

    /// <summary>Lets the held calls go on (each starts its timeout now), and every call after them at once.</summary>
    public void Release()
    {
        TaskCompletionSource? released;
        lock (_gate)
        {
            released = _released;
            _released = null;
        }

        released?.TrySetResult();
    }

    /// <summary>Completes once <paramref name="count"/> calls have started waiting for their timeout.</summary>
    public Task TimeoutWaitsReachedAsync(int count)
    {
        lock (_gate)
        {
            if (_waiting >= count)
                return Task.CompletedTask;

            var reached = NewSignal();
            _waitingSignals.Add((count, reached));
            return reached.Task;
        }
    }

    public async Task<Result<TResponse>> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(request);
        var requests = new Dictionary<string, TRequest>(StringComparer.Ordinal) { [RequestKey] = request };
        var (call, started) = await ArriveAsync(ScatterGatherCallKind.Request, requests, timeout, cancellationToken);
        if (Forwarded() is { } inner)
            return await inner.RequestAsync<TRequest, TResponse>(request, timeout, cancellationToken);

        var gathered = await AnswerAsync<TRequest, TResponse>(requests, call, started, cancellationToken);
        return gathered[RequestKey];
    }

    public async Task<IGathered<TResponse>> GatherAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(requests);
        var (call, started) = await ArriveAsync(ScatterGatherCallKind.Gather, requests, timeout, cancellationToken);
        if (Forwarded() is { } inner)
            return await inner.GatherAsync<TRequest, TResponse>(requests, timeout, cancellationToken);

        return await AnswerAsync<TRequest, TResponse>(requests, call, started, cancellationToken);
    }

    public IAsyncEnumerable<IScatterReply<TResponse>> StreamAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(requests);
        return StreamRepliesAsync<TRequest, TResponse>(requests, timeout, cancellationToken);
    }

    public IAsyncEnumerable<GatheredChunk<TResponse>> GatherInChunksAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        int chunkSize,
        Func<int, TimeSpan> timeoutForChunk,
        TimeSpan? budget = null,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
        => ChunkedGathering.GatherAsync<TRequest, TResponse>(
            this,
            requests,
            chunkSize,
            timeoutForChunk,
            budget,
            _time,
            cancellationToken);

    /// <summary>From now on every call goes to <paramref name="inner"/> (the host's real client) once recorded and let through.</summary>
    internal void ForwardTo(IScatterGatherClient inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        lock (_gate)
            _forwardTo = inner;
    }

    // The replies a gather of the same items gives, as they would stream: every item that answered, in key order; one
    // that never answers isn't yielded (the stream ends at the timeout without it).
    private async IAsyncEnumerable<IScatterReply<TResponse>> StreamRepliesAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        var (call, started) = await ArriveAsync(ScatterGatherCallKind.Stream, requests, timeout, cancellationToken);
        if (Forwarded() is { } inner)
        {
            await foreach (var reply in inner.StreamAsync<TRequest, TResponse>(requests, timeout, cancellationToken))
                yield return reply;
            yield break;
        }

        var gathered = await AnswerAsync<TRequest, TResponse>(requests, call, started, cancellationToken);
        foreach (var key in call.Keys.Where(key => gathered.Results.ContainsKey(key) || gathered.Failures.ContainsKey(key)))
            yield return new ScatterReply<TResponse>(key, gathered[key]);
    }

    // Records the call, runs OnCall, throws if told to, and waits while calls are held. Its timeout starts as it arrives
    // (before OnCall, which may move the clock past it), or, when it was held, as it is let go.
    private async Task<(ScatterGatherCall Call, long Started)> ArriveAsync<TRequest>(
        ScatterGatherCallKind kind,
        IReadOnlyDictionary<string, TRequest> requests,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        var started = _time.GetTimestamp();
        var call = new ScatterGatherCall(
            kind,
            typeof(TRequest),
            [.. requests.Keys],
            timeout,
            requests.ToDictionary(request => request.Key, request => (object)request.Value, StringComparer.Ordinal));
        Exception? failure;
        lock (_gate)
        {
            var number = _calls.Count;
            _calls.Add(call);
            failure = FailureFor(number);
        }

        OnCall?.Invoke(call);
        if (failure is not null)
            throw failure;

        if (await PassHoldAsync(cancellationToken))
            started = _time.GetTimestamp();
        return (call, started);
    }

    // Under the gate: the failure scripted for this call by its number, else the next one queued, if any.
    private Exception? FailureFor(int call)
    {
        if (_throwOnCall.Remove(call, out var numbered))
            return numbered;

        return _throwOnNextCalls.TryDequeue(out var next) ? next : null;
    }

    // True when the call was held, and is let go now.
    private async Task<bool> PassHoldAsync(CancellationToken cancellationToken)
    {
        Task released;
        lock (_gate)
        {
            if (_released is null)
                return false;
            released = _released.Task;
            _held.TrySetResult();
        }

        await released.WaitAsync(cancellationToken);
        return true;
    }

    private IScatterGatherClient? Forwarded()
    {
        lock (_gate)
            return _forwardTo;
    }

    // Each item as scripted; with one that never answers, the call ends at its timeout on the test's clock.
    private async Task<IGathered<TResponse>> AnswerAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        ScatterGatherCall call,
        long started,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        var results = new Dictionary<string, TResponse>(StringComparer.Ordinal);
        var failures = new Dictionary<string, IReadOnlyList<Error>>(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var (key, request) in requests)
            {
                switch (ScriptFor<TRequest>(key, request))
                {
                    case ScriptedFailure failure:
                        failures[key] = failure.Errors;
                        break;
                    case TResponse response:
                        results[key] = response;
                        break;
                    case null or ScriptedSilence:
                        break;
                    case var other:
                        throw new InvalidOperationException(
                            $"{typeof(TRequest).Name} item '{key}' is scripted to answer {other.GetType().Name}, but the " +
                            $"call asks for {typeof(TResponse).Name}.");
                }
            }
        }

        var gathered = new Gathered<TResponse>(call.Keys, results, failures);
        if (!gathered.IsComplete)
            await WaitOutTimeoutAsync(call.Timeout - _time.GetElapsedTime(started), cancellationToken);
        return gathered;
    }

    private object? ScriptFor<TRequest>(
        string key,
        TRequest request)
        where TRequest : class
    {
        if (_silenced.Contains(typeof(TRequest)))
            return null;
        if (_byKey.TryGetValue((typeof(TRequest), key), out var scripted))
            return scripted;
        return _everyItem.TryGetValue(typeof(TRequest), out var every)
            ? every(key, request)
            : null;
    }

    private async Task WaitOutTimeoutAsync(
        TimeSpan remaining,
        CancellationToken cancellationToken)
    {
        SignalWaiting();
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, _time, cancellationToken);
    }

    private void SignalWaiting()
    {
        List<TaskCompletionSource> reached = [];
        lock (_gate)
        {
            _waiting++;
            foreach (var signal in _waitingSignals.FindAll(signal => signal.Count <= _waiting))
            {
                _waitingSignals.Remove(signal);
                reached.Add(signal.Reached);
            }
        }

        foreach (var signal in reached)
            signal.TrySetResult();
    }

    private ScatterGatherClientStandIn Script<TRequest>(
        string key,
        object script)
    {
        lock (_gate)
            _byKey[(typeof(TRequest), key)] = script;
        return this;
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed record ScriptedFailure(IReadOnlyList<Error> Errors);

    private sealed class ScriptedSilence
    {
        public static readonly ScriptedSilence Instance = new();
    }
}
