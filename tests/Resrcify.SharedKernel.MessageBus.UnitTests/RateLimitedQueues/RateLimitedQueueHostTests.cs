using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.RateLimitedQueues;

/// <summary>A responder on the in-memory transport: requests whose requester stopped waiting aren't handled.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class RateLimitedQueueHostTests
{
    [Fact]
    public async Task HandleAsync_ShouldNotHandleARequest_WhenItExpiredWhileWaitingForTheQueue()
    {
        using var metrics = new MetricsCapture();
        var network = new InMemNetwork();
        var queue = $"slow-{Guid.NewGuid():N}";
        var calls = new SlowCalls();
        using var responder = await InMemoryServices.StartAsync(
            network,
            // One at a time: the second request waits while the first one is handled.
            bus => bus.AddRateLimitedQueue<SlowRequest, SlowResponse, SlowHandler>(queue, options => options.Prefetch = 1),
            services => services.AddSingleton(calls));
        using var requester = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRequest<SlowRequest, SlowResponse>(queue).AddScatterGather());

        var gathered = await requester.Services.GetRequiredService<IScatterGatherClient>().GatherAsync<SlowRequest, SlowResponse>(
            new Dictionary<string, SlowRequest> { ["a"] = new("a"), ["b"] = new("b") },
            TimeSpan.FromMilliseconds(300));
        // The first is cancelled mid-handling when its requester stops waiting; the second has expired by then, so it
        // is dropped (by the transport, or by the queue's own deadline check) instead of handled.
        await InMemoryServices.WaitUntilAsync(() => metrics.Count($"messagebus.requests.handled outcome=expired queue={queue}") >= 1);
        await Task.Delay(TimeSpan.FromMilliseconds(800));

        gathered.UnansweredKeys.Count.ShouldBe(2);
        calls.Count.ShouldBe(1);
    }

    internal sealed record SlowRequest(string Key);

    internal sealed record SlowResponse(string Key);

    internal sealed class SlowCalls
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Add()
            => Interlocked.Increment(ref _count);
    }

    internal sealed class SlowHandler(SlowCalls calls) : IRequestResponder<SlowRequest, SlowResponse>
    {
        public async Task<Result<SlowResponse>> HandleAsync(SlowRequest request, CancellationToken cancellationToken = default)
        {
            calls.Add();
            await Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken);
            return new SlowResponse(request.Key);
        }
    }
}
