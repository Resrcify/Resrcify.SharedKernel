using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests;

/// <summary>
/// Publish/subscribe over real RabbitMQ: services subscribe by having an <c>IIntegrationEventHandler</c>, each
/// service gets one copy that one of its instances handles, and an event that keeps failing ends in the service's
/// own error queue.
/// </summary>
[Collection(BusCollection.Name)]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class PublishSubscribeTests(BusFixture bus, ITestOutputHelper output)
{
    private readonly string _wireName = $"test.player-renamed.{Guid.NewGuid():N}.v1";

    [Fact]
    public async Task Publish_WhenTwoServicesSubscribe_EachGetsACopyUnderItsOwnClass()
    {
        await using var shard = await EventService.StartSubscriberAsync(bus.RabbitMqConnection, _wireName, Queue("shard"));
        await using var discord = await EventService.StartSubscriberAsync(bus.RabbitMqConnection, _wireName, Queue("discord"));
        await using var publisher = await EventService.StartPublisherAsync(bus.RabbitMqConnection, _wireName);

        await publisher.PublishAsync(new PlayerRenamedPublished("p1", "Han"));
        await WaitUntilAsync(() => shard.Log.Handled.Count == 1 && discord.Log.Handled.Count == 1);

        shard.Log.Handled.Single().ShouldBe(new PlayerRenamed("p1", "Han", Fail: false));
        discord.Log.Handled.Single().ShouldBe(new PlayerRenamed("p1", "Han", Fail: false));
    }

    [Fact]
    public async Task Publish_WhenTheBrokerRefusesTheEvent_Throws()
    {
        // Publisher confirms: the publish waits for the broker, so an outbox retries a refused event instead of
        // marking it sent.
        await BrokerQueues.DeclareRefusingSubscriberAsync(bus.RabbitMqConnection, _wireName, Queue("refusing"));
        await using var publisher = await EventService.StartPublisherAsync(bus.RabbitMqConnection, _wireName);

        await Should.ThrowAsync<Exception>(() => publisher.PublishAsync(new PlayerRenamedPublished("p1", "Han")));
    }

    [Fact]
    public async Task Publish_WithPublisherConfirmsOff_ReturnsWhenTheBrokerRefusesTheEvent()
    {
        await BrokerQueues.DeclareRefusingSubscriberAsync(bus.RabbitMqConnection, _wireName, Queue("refusing"));
        await using var publisher = await EventService.StartPublisherAsync(
            bus.RabbitMqConnection,
            _wireName,
            configure => configure.UseConfigurationStrategy(new PublisherConfirmsOffStrategy()));

        await Should.NotThrowAsync(() => publisher.PublishAsync(new PlayerRenamedPublished("p1", "Han")));
    }

    [Fact]
    public async Task Publish_WhenAServiceRunsTwoInstances_OneOfThemHandlesEachEvent()
    {
        var queue = Queue("shard");
        await using var first = await EventService.StartSubscriberAsync(bus.RabbitMqConnection, _wireName, queue);
        await using var second = await EventService.StartSubscriberAsync(bus.RabbitMqConnection, _wireName, queue);
        await using var publisher = await EventService.StartPublisherAsync(bus.RabbitMqConnection, _wireName);

        for (var i = 0; i < 20; i++)
            await publisher.PublishAsync(new PlayerRenamedPublished($"p{i}", "Han"));
        await WaitUntilAsync(() => first.Log.Handled.Count + second.Log.Handled.Count >= 20);
        await Task.Delay(TimeSpan.FromSeconds(1));

        output.WriteLine($"first instance {first.Log.Handled.Count}, second {second.Log.Handled.Count}");
        var handled = first.Log.Handled.Concat(second.Log.Handled).ToList();
        handled.Count.ShouldBe(20);
        handled.Select(renamed => renamed.PlayerId).Distinct().Count().ShouldBe(20);
    }

    [Fact]
    public async Task Publish_WhenAServiceHandlesEventsInPublishOrder_OnlyOneOfItsInstancesConsumes()
    {
        var queue = Queue("shard");
        await using var first = await EventService.StartSubscriberAsync(bus.RabbitMqConnection, _wireName, queue, InPublishOrder);
        await using var second = await EventService.StartSubscriberAsync(bus.RabbitMqConnection, _wireName, queue, InPublishOrder);
        await using var publisher = await EventService.StartPublisherAsync(bus.RabbitMqConnection, _wireName);

        for (var i = 0; i < 20; i++)
            await publisher.PublishAsync(new PlayerRenamedPublished($"p{i}", "Han"));
        await WaitUntilAsync(() => first.Log.Handled.Count + second.Log.Handled.Count >= 20);
        await Task.Delay(TimeSpan.FromSeconds(1));

        output.WriteLine($"first instance {first.Log.Handled.Count}, second {second.Log.Handled.Count}");
        new[] { first.Log.Handled.Count, second.Log.Handled.Count }.ShouldBe([0, 20], ignoreOrder: true);
    }

    [Fact]
    public async Task Start_WhenAnotherServiceSendsToAnOrderedSubscribersQueue_StartsAndDelivers()
    {
        // The owner declares its queue with x-single-active-consumer; a sender declaring it again with Rebus' defaults
        // was refused (406 inequivalent arg) and failed to start after a minute.
        var queue = Queue("discord");
        await using var owner = await EventService.StartSubscriberAsync(bus.RabbitMqConnection, _wireName, queue, InPublishOrder);

        var started = DateTimeOffset.UtcNow;
        await using var sender = await EventService.StartSenderAsync(bus.RabbitMqConnection, _wireName, queue);
        await sender.SendAsync(new PlayerRenamedPublished("p1", "Han"));

        (DateTimeOffset.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(30));
        await WaitUntilAsync(() => !owner.Log.Handled.IsEmpty);
    }

    [Fact]
    public async Task Publish_WhenAnEventInPublishOrderKeepsFailing_RetriesItInPlaceThenTheNextOnesFollowInOrder()
    {
        var queue = Queue("shard");
        await using var shard = await EventService.StartSubscriberAsync(bus.RabbitMqConnection, _wireName, queue, InPublishOrder);
        await using var publisher = await EventService.StartPublisherAsync(bus.RabbitMqConnection, _wireName);

        await publisher.PublishAsync(new PlayerRenamedPublished("p1", "first", Fail: true));
        await publisher.PublishAsync(new PlayerRenamedPublished("p1", "second"));
        await publisher.PublishAsync(new PlayerRenamedPublished("p1", "third"));
        await WaitUntilAsync(() => shard.Log.Handled.Count == 2);
        await WaitUntilAsync(async () => await BrokerQueues.MessageCountAsync(bus.RabbitMqConnection, $"{queue}.error") == 1);

        // Tried 5 times in its place (the 2 others ran after it), then moved to the error queue at once.
        shard.Log.Attempts.ShouldBe(5 + 2);
        shard.Log.Handled.Select(renamed => renamed.Name).ShouldBe(["second", "third"]);
    }

    [Fact]
    public async Task Publish_WhenAHandlerKeepsFailing_MovesTheEventToTheServicesErrorQueue()
    {
        var queue = Queue("shard");
        await using var shard = await EventService.StartSubscriberAsync(bus.RabbitMqConnection, _wireName, queue);
        await using var publisher = await EventService.StartPublisherAsync(bus.RabbitMqConnection, _wireName);

        await publisher.PublishAsync(new PlayerRenamedPublished("p1", "Han", Fail: true));
        // Rebus tries an event 5 times before giving up on it.
        await WaitUntilAsync(() => shard.Log.Attempts >= 5);
        await WaitUntilAsync(async () => await BrokerQueues.MessageCountAsync(bus.RabbitMqConnection, $"{queue}.error") == 1);

        (await BrokerQueues.MessageCountAsync(bus.RabbitMqConnection, $"{queue}.error")).ShouldBe(1);
        shard.Log.Attempts.ShouldBe(5);
    }

    [Fact]
    public async Task Publish_WhenEventsShareAPartitionKey_HandlesThemOneAtATimeAndOthersAlongside()
    {
        var log = new EventLog { Delay = TimeSpan.FromMilliseconds(50) };
        await using var shard = await EventService.StartSubscriberAsync(
            bus.RabbitMqConnection,
            _wireName,
            Queue("shard"),
            subscriber => subscriber.ConfigureSubscription<PlayerRenamed>(options => options.HandleInPartitions(renamed => renamed.PlayerId)),
            log);
        await using var publisher = await EventService.StartPublisherAsync(bus.RabbitMqConnection, _wireName);

        for (var i = 0; i < 8; i++)
        {
            await publisher.PublishAsync(new PlayerRenamedPublished("same-player", $"name {i}"));
            await publisher.PublishAsync(new PlayerRenamedPublished($"player-{i}", "name"));
        }
        await WaitUntilAsync(() => log.Handled.Count == 16);

        output.WriteLine($"most at once {log.MostAtOnce}, for one player {log.MostAtOnceForOnePlayer}");
        log.MostAtOnceForOnePlayer.ShouldBe(1);
        log.MostAtOnce.ShouldBeGreaterThan(1);
        // In publish order, though up to 20 events are handled at once.
        log.Handled.Where(renamed => renamed.PlayerId == "same-player").Select(renamed => renamed.Name)
            .ShouldBe(Enumerable.Range(0, 8).Select(i => $"name {i}"));
    }

    [Fact]
    public async Task Publish_WhenTheOutboxRetriesAfterPublishing_TheSubscriberHandlesTheEventOnce()
    {
        await using var shard = await EventService.StartSubscriberAsync(
            bus.RabbitMqConnection,
            _wireName,
            Queue("discord"),
            subscriber => subscriber.SkipDuplicateEvents());
        await using var requester = await RequesterHost.StartAsync(
            bus.RabbitMqConnection,
            bus.Postgres.CreateIsolatedConnectionString(),
            $"ping-{Guid.NewGuid():N}",
            configureBus: publisher => publisher.AddMessage<PlayerRenamedPublished>(_wireName));

        var noteId = await requester.CreateNoteAsync(shared: true);
        // The handler publishes, then fails; the outbox retries it and it publishes again under the same message ID.
        await WaitUntilAsync(() => RequesterHost.NoteSharedHandler.Attempts.TryGetValue(noteId, out var attempts) && attempts >= 2);
        await WaitUntilAsync(() => shard.Log.Attempts >= 1);
        await Task.Delay(TimeSpan.FromSeconds(1));

        shard.Log.Handled.Count.ShouldBe(1);
        shard.Log.Handled.Single().PlayerId.ShouldBe(noteId.ToString("N"));
    }

    /// <summary>Handles the test's event per player, in publish order (the default for partitions).</summary>
    private static void InPublishOrder(MessageBusBuilder subscriber)
        => subscriber.ConfigureSubscription<PlayerRenamed>(options => options.HandleInPartitions(renamed => renamed.PlayerId));

    private static string Queue(string service)
        => $"{service}-{Guid.NewGuid():N}";

    private static Task WaitUntilAsync(Func<bool> condition)
        => WaitUntilAsync(() => Task.FromResult(condition()));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var giveUpAt = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            if (DateTime.UtcNow > giveUpAt)
                throw new TimeoutException("Condition was not met within 30 s.");
            await Task.Delay(100);
        }
    }
}
