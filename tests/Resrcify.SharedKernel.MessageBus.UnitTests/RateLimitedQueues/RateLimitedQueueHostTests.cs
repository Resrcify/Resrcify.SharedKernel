using System;
using System.Collections.Concurrent;
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
using Resrcify.SharedKernel.MessageBus.UnitTests.Hosting;
using Resrcify.SharedKernel.MessageBus.UnitTests.Strategies;
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
        var client = requester.Services.GetRequiredService<IScatterGatherClient>();

        // The first request to arrive holds the queue's only slot until the test lets it go, so the other waits behind it.
        var gathered = await client.GatherAsync<SlowRequest, SlowResponse>(
            new Dictionary<string, SlowRequest> { ["a"] = new("a"), ["b"] = new("b") },
            TimeSpan.FromMilliseconds(300));
        // The gather returning means the requester stopped waiting. The second was sent a moment after the gather
        // started, so its own expiry can be just behind: this margin only ever has to be long enough, never short enough.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        calls.ReleaseHeld();
        // A fresh request behind them: once it is handled, the queue has taken the expired one (one at a time, in
        // order), so it was dropped (by the transport, or by the queue's own deadline check), not just not reached yet.
        var marker = await client.GatherAsync<SlowRequest, SlowResponse>(
            new Dictionary<string, SlowRequest> { ["c"] = new("c") },
            TimeSpan.FromSeconds(30));

        gathered.UnansweredKeys.Count.ShouldBe(2);
        marker.UnansweredKeys.ShouldBeEmpty();
        calls.Handled.Count.ShouldBe(2);
        calls.Handled[0].ShouldBeOneOf("a", "b");
        calls.Handled[1].ShouldBe("c");
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

    [Fact]
    public async Task ExecuteAsync_ShouldStartTheBusAgain_WhenItFailedToStart()
    {
        // The queue can't be declared at first (RabbitMQ unreachable while the queue's health gate says healthy): the
        // host used to fault, stopping the service, or leave the queue unconsumed for good.
        var network = new InMemNetwork();
        var queue = $"flaky-{Guid.NewGuid():N}";
        var strategy = new FailingStartStrategy(address => address == queue);
        strategy.Arm(1);
        using var responder = await InMemoryServices.StartAsync(
            network,
            bus => bus
                .UseConfigurationStrategy(strategy)
                .AddRateLimitedQueue<SlowRequest, SlowResponse, RegularHandler>(queue));
        using var requester = await InMemoryServices.StartAsync(network, bus => bus.AddRequest<SlowRequest, SlowResponse>(queue).AddScatterGather());
        var consumer = responder.Services.GetServices<IQueueConsumer>().Single(registered => registered.QueueName == queue);

        await InMemoryServices.WaitUntilAsync(() => strategy.Failed == 1);
        consumer.IsConsuming.ShouldBeFalse();   // nothing half-started counts as consuming

        // Tried again 5 s later, on the container's clock.
        await InMemoryServices.WaitUntilAsync(() => consumer.IsConsuming, TimeSpan.FromSeconds(15));
        var reply = await requester.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<SlowRequest, SlowResponse>(new SlowRequest("a"), TimeSpan.FromSeconds(5));

        reply.Value.Key.ShouldBe("regular a");
        strategy.Failed.ShouldBe(1);
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

    /// <summary>
    /// The requests <see cref="SlowHandler"/> handled, in order. It holds the first one until <see cref="ReleaseHeld"/>.
    /// </summary>
    internal sealed class SlowCalls
    {
        private readonly ConcurrentQueue<string> _handled = new();
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public IReadOnlyList<string> Handled => [.. _handled];

        /// <summary>Records the request; returns a task that completes when it may finish (at once, but for the first).</summary>
        public Task AddAsync(string key)
        {
            _handled.Enqueue(key);
            return Interlocked.Increment(ref _count) == 1
                ? _released.Task
                : Task.CompletedTask;
        }

        public void ReleaseHeld()
            => _released.TrySetResult();
    }

    internal sealed class SlowHandler(SlowCalls calls) : IRequestResponder<SlowRequest, SlowResponse>
    {
        public async Task<Result<SlowResponse>> HandleAsync(SlowRequest request, CancellationToken cancellationToken = default)
        {
            // Not cancelled with its requester: the first request keeps the slot until the test is sure the one
            // waiting behind it has expired.
            await calls.AddAsync(request.Key);
            return new SlowResponse(request.Key);
        }
    }
}
