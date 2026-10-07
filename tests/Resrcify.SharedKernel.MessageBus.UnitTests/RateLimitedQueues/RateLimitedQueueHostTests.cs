using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
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

    [Fact]
    public async Task AddRateLimitedQueue_ShouldConsumeEachQueueWithItsOwnHandler_WhenTwoShareTheRequestTypes()
    {
        var network = new InMemNetwork();
        var regular = $"guild-{Guid.NewGuid():N}";
        var priority = $"guild-priority-{Guid.NewGuid():N}";
        using var responder = await InMemoryServices.StartAsync(
            network,
            bus => bus
                .AddRateLimitedQueue<SlowRequest, SlowResponse, RegularHandler>(regular)
                .AddRateLimitedQueue<SlowRequest, SlowResponse, PriorityHandler>(priority));
        using var regularRequester = await InMemoryServices.StartAsync(network, bus => bus.AddRequest<SlowRequest, SlowResponse>(regular).AddScatterGather());
        using var priorityRequester = await InMemoryServices.StartAsync(network, bus => bus.AddRequest<SlowRequest, SlowResponse>(priority).AddScatterGather());

        var fromRegular = await regularRequester.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<SlowRequest, SlowResponse>(new SlowRequest("a"), TimeSpan.FromSeconds(5));
        var fromPriority = await priorityRequester.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<SlowRequest, SlowResponse>(new SlowRequest("b"), TimeSpan.FromSeconds(5));

        fromRegular.Value.Key.ShouldBe("regular a");
        fromPriority.Value.Key.ShouldBe("priority b");
        responder.Services.GetServices<IQueueConsumer>().Select(queue => queue.QueueName).ShouldBe([regular, priority], ignoreOrder: true);
    }

    [Fact]
    public void AddRateLimitedQueue_ShouldThrow_WhenAQueueNameIsUsedTwice()
        => Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddMessageBus(bus => bus
            .UseInMemory(new InMemNetwork())
            .AddRateLimitedQueue<SlowRequest, SlowResponse, RegularHandler>("guild")
            .AddRateLimitedQueue<SlowRequest, SlowResponse, PriorityHandler>("guild")));

    [Fact]
    public async Task HandleAsync_ShouldGiveTheResponderTheQueuesBus_WhenItTakesTheEventBus()
    {
        var network = new InMemNetwork();
        var queue = $"publishing-{Guid.NewGuid():N}";
        using var responder = await InMemoryServices.StartAsync(
            network,
            bus => bus.AddRateLimitedQueue<SlowRequest, SlowResponse, PublishingHandler>(queue));
        using var requester = await InMemoryServices.StartAsync(network, bus => bus.AddRequest<SlowRequest, SlowResponse>(queue).AddScatterGather());

        var reply = await requester.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<SlowRequest, SlowResponse>(new SlowRequest("a"), TimeSpan.FromSeconds(5));

        // It threw ("Couldn't find IBus in the incoming step context") on every try, and was answered ResponderFailed.
        reply.IsSuccess.ShouldBeTrue();
        reply.Value.Key.ShouldBe("published a");
    }

    internal sealed class RegularHandler : IRequestResponder<SlowRequest, SlowResponse>
    {
        public Task<Result<SlowResponse>> HandleAsync(SlowRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success(new SlowResponse($"regular {request.Key}")));
    }

    internal sealed class PriorityHandler : IRequestResponder<SlowRequest, SlowResponse>
    {
        public Task<Result<SlowResponse>> HandleAsync(SlowRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success(new SlowResponse($"priority {request.Key}")));
    }

    internal sealed class PublishingHandler(IEventBus events) : IRequestResponder<SlowRequest, SlowResponse>
    {
        public Task<Result<SlowResponse>> HandleAsync(SlowRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success(new SlowResponse(events is null ? "none" : $"published {request.Key}")));
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
