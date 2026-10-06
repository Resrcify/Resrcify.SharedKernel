using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.ScatterGather;

/// <summary>Answers every item at once with <paramref name="answer"/>; records what was sent and what was streamed.</summary>
internal sealed class FakeScatterGatherTransport(Func<string, object> answer) : IScatterGatherClient
{
    public ConcurrentDictionary<string, object> Sent { get; } = new();

    public List<string> Streamed { get; } = [];

    public async Task<Result<TResponse>> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        var gathered = await GatherAsync<TRequest, TResponse>(
            new Dictionary<string, TRequest> { ["request"] = request },
            timeout,
            cancellationToken);
        return gathered["request"];
    }

    public Task<IGathered<TResponse>> GatherAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        foreach (var (key, request) in requests)
            Sent[key] = request;
        var results = requests.Keys.ToDictionary(key => key, key => (TResponse)answer(key));
        return Task.FromResult<IGathered<TResponse>>(new Gathered<TResponse>(requests.Keys.ToList(), results, new Dictionary<string, IReadOnlyList<Error>>()));
    }

    public async IAsyncEnumerable<IScatterReply<TResponse>> StreamAsync<TRequest, TResponse>(
        IReadOnlyDictionary<string, TRequest> requests,
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        foreach (var (key, request) in requests)
            Sent[key] = request;
        foreach (var key in requests.Keys)
        {
            await Task.Yield();
            Streamed.Add(key);
            yield return new ScatterReply<TResponse>(key, (TResponse)answer(key));
        }
    }
}
