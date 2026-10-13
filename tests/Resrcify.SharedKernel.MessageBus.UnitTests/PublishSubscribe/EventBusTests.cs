using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Caching.Primitives;
using Resrcify.SharedKernel.MessageBus.PublishSubscribe;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.PublishSubscribe;

/// <summary>The message IDs <see cref="EventBus"/> gives events published while the outbox handles a message.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class EventBusTests
{
    [Fact]
    public void StableIdKind_ShouldBeTheSame_WhenTheContentIsTheSame()
        => EventBus.StableIdKind("PayoutRotated", new PayoutRotated("shard-1", 1))
            .ShouldBe(EventBus.StableIdKind("PayoutRotated", new PayoutRotated("shard-1", 1)));

    [Fact]
    public void StableIdKind_ShouldDiffer_WhenTheContentDiffers()
        => EventBus.StableIdKind("PayoutRotated", new PayoutRotated("shard-1", 1))
            .ShouldNotBe(EventBus.StableIdKind("PayoutRotated", new PayoutRotated("shard-2", 1)));

    [Fact]
    public void StableIdKind_ShouldIgnoreContentComputedWhileHandling_WhenTheEventNamesItsSubject()
        => EventBus.StableIdKind("RankChanged", new RankChanged("shard-1", TimeSpan.FromMinutes(90)))
            .ShouldBe(EventBus.StableIdKind("RankChanged", new RankChanged("shard-1", TimeSpan.FromMinutes(89))));

    [Fact]
    public void StableIdKind_ShouldDiffer_WhenTheSubjectsDiffer()
        => EventBus.StableIdKind("RankChanged", new RankChanged("shard-1", TimeSpan.Zero))
            .ShouldNotBe(EventBus.StableIdKind("RankChanged", new RankChanged("shard-2", TimeSpan.Zero)));

    [Fact]
    public async Task PublishAsync_ShouldPublishUnderTheEventsOwnName_WhenTheCallerDeclaresABaseType()
    {
        using var metrics = new MetricsCapture();
        using var publisher = await InMemoryServices.StartAsync(new InMemNetwork(), _ => { });

        object integrationEvent = new BaseTyped(1);
        await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(integrationEvent);

        // Under "Object", which nobody binds, it was dropped.
        metrics.Count("messagebus.events.published event=BaseTyped via_outbox=false").ShouldBe(1);
    }

    [Fact]
    public void StableIdKind_ShouldFallBackToTheTopic_WhenTheContentCannotBeSerialized()
    {
        var looped = new Looped();
        looped.Self = looped;

        EventBus.StableIdKind("Looped", looped).ShouldBe("Looped");
    }

    [Fact]
    public async Task PublishAsync_ShouldDeliverEveryEvent_WhenARetryPublishesThemInAnotherOrder()
    {
        // The first attempt published one event and failed; the retry publishes both, the other one first
        // (as handlers publishing in parallel can). Each event keeps its ID: the repeat is skipped, the new one isn't.
        var network = new InMemNetwork();
        var log = new PayoutLog();
        using var subscriber = await InMemoryServices.StartAsync(
            network,
            bus => bus
                .WithInputQueue($"subscriber-{Guid.NewGuid():N}")
                .AddEventHandlers(typeof(EventBusTests).Assembly)
                .SkipDuplicateEvents(),
            services => services
                .AddSingleton(log)
                .AddDistributedMemoryCache()
                .AddSingleton<ICachingService, DistributedCachingService>());
        var outbox = new FakeOutboxMessage();
        using var publisher = await InMemoryServices.StartAsync(
            network,
            _ => { },
            services => services.AddSingleton<IOutboxMessageContext>(outbox));
        var eventBus = publisher.Services.GetRequiredService<IEventBus>();
        var outboxMessageId = Guid.NewGuid();
        using var metrics = new MetricsCapture();

        using (outbox.Handle(outboxMessageId))
            await eventBus.PublishAsync(new PayoutRotated("shard-1", 1));
        await InMemoryServices.WaitUntilAsync(() => log.Handled.Count == 1);

        using (outbox.Handle(outboxMessageId))
        {
            await eventBus.PublishAsync(new PayoutRotated("shard-2", 1));
            await eventBus.PublishAsync(new PayoutRotated("shard-1", 1));
        }
        await InMemoryServices.WaitUntilAsync(() => metrics.Count("messagebus.events.handled event=PayoutRotated outcome=duplicate") >= 1);
        await InMemoryServices.WaitUntilAsync(() => log.Handled.Count >= 2);

        log.Handled.Select(payout => payout.ShardId).Order().ShouldBe(["shard-1", "shard-2"]);
    }

    [Fact]
    public async Task PublishAsync_ShouldTagWhetherTheEventLeftThroughTheOutbox_WhenItIsPublished()
    {
        using var metrics = new MetricsCapture();
        var outbox = new FakeOutboxMessage();
        using var publisher = await InMemoryServices.StartAsync(
            new InMemNetwork(),
            _ => { },
            services => services.AddSingleton<IOutboxMessageContext>(outbox));
        var eventBus = publisher.Services.GetRequiredService<IEventBus>();

        // An event only this test publishes: the meter is shared with the tests running alongside it.
        using (outbox.Handle(Guid.NewGuid()))
            await eventBus.PublishAsync(new OutboxTagged(1));   // from a domain event handler, via the outbox
        await eventBus.PublishAsync(new OutboxTagged(2));       // from anywhere else

        metrics.Count("messagebus.events.published event=OutboxTagged via_outbox=true").ShouldBe(1);
        metrics.Count("messagebus.events.published event=OutboxTagged via_outbox=false").ShouldBe(1);
    }

    private sealed class Looped
    {
        public Looped? Self { get; set; }
    }

    private sealed record OutboxTagged(int Sequence);

    private sealed record BaseTyped(int Sequence);

    /// <summary>An event with a field computed from the clock while handling; its subject is the shard.</summary>
    private sealed record RankChanged(string ShardId, TimeSpan TimeUntilPayout) : IDedupable
    {
        public string DedupKey => ShardId;
    }
}
