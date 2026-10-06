using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;

/// <summary>A query answered by asking the responders: how many of <see cref="Items"/> pings got a pong.</summary>
internal sealed record CountPongsQuery(int Items) : IRequest<int>;

/// <summary>The first <see cref="Wanted"/> pongs to arrive, in arrival order, out of <see cref="Items"/> pings.</summary>
internal sealed record FirstPongsQuery(int Items, int Wanted, TimeSpan Timeout) : IRequest<IReadOnlyList<string>>;

internal sealed class CountPongsQueryHandler : IScatterGatherRequestHandler<CountPongsQuery, PingRequest, PingResponse, int>
{
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public Task<IReadOnlyDictionary<string, PingRequest>> ScatterAsync(CountPongsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(Pings(request.Items));
    }

    public Task<int> HandleAsync(CountPongsQuery request, IGathered<PingResponse> replies, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replies);
        return Task.FromResult(replies.Results.Count);
    }

    internal static IReadOnlyDictionary<string, PingRequest> Pings(int items)
        => Enumerable.Range(0, items).ToDictionary(i => $"item-{i}", i => new PingRequest(string.Create(CultureInfo.InvariantCulture, $"ping {i}")));
}

internal sealed class FirstPongsQueryHandler : IStreamingScatterGatherRequestHandler<FirstPongsQuery, PingRequest, PingResponse, IReadOnlyList<string>>
{
    /// <summary>Per query: the handler is resolved once per send, so it reads the request's own timeout.</summary>
    public static TimeSpan CurrentTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan Timeout => CurrentTimeout;

    public Task<IReadOnlyDictionary<string, PingRequest>> ScatterAsync(FirstPongsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        CurrentTimeout = request.Timeout;
        return Task.FromResult(CountPongsQueryHandler.Pings(request.Items));
    }

    public async Task<IReadOnlyList<string>> HandleAsync(FirstPongsQuery request, IAsyncEnumerable<IScatterReply<PingResponse>> replies, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(replies);
        var arrived = new List<string>();
        await foreach (var reply in replies.WithCancellation(cancellationToken))
        {
            arrived.Add(reply.Key);
            if (arrived.Count == request.Wanted)
                break;
        }
        return arrived;
    }
}
