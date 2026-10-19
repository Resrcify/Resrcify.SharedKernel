using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Rebus.Config;
using Rebus.Retry.Simple;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.IntegrationTests.Broker;
using Resrcify.SharedKernel.MessageBus.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.MessageBus.IntegrationTests.Hosts;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests;

/// <summary>
/// The error queue over real RabbitMQ, on classic queues and on quorum queues (a production cluster's default): the
/// events that kept failing are shown without being used up, deleted, or moved back and handled.
/// </summary>
[Collection(BusCollection.Name)]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class ErrorQueueTests(BusFixture bus)
{
    private readonly string _wireName = $"test.player-renamed.{Guid.NewGuid():N}.v1";

    [Fact]
    public async Task TheErrorQueue_ShowsDeletesAndMovesBack_OnClassicQueues()
        => await ShowsDeletesAndMovesBackAsync(bus.RabbitMqConnection);

    [Fact]
    public async Task TheErrorQueue_ShowsDeletesAndMovesBack_OnQuorumQueues()
    {
        var quorum = await bus.QuorumVirtualHostAsync();

        var errorQueue = await ShowsDeletesAndMovesBackAsync(quorum);

        (await bus.QueueTypeAsync(quorum.VirtualHost, errorQueue)).ShouldBe("quorum");
    }

    [Fact]
    public async Task Listing_ShouldNotUseUpAQuorumQueuesDeliveryLimit()
    {
        var quorum = await bus.QuorumVirtualHostAsync();
        var queue = Queue("shard");
        await using var shard = await StartFailingSubscriberAsync(quorum, queue);
        await FailAsync(quorum, shard, queue, "p1");

        // A quorum queue drops a message after 20 deliveries unless it was handed back explicitly.
        for (var i = 0; i < 25; i++)
            (await shard.ErrorQueue.ListAsync()).Value.Count.ShouldBe(1);

        (await shard.ErrorQueue.ListAsync()).Value.Messages.ShouldHaveSingleItem().Text!.ShouldContain("p1");
    }

    [Fact]
    public async Task RetryAsync_ShouldKeepTheMessage_WhenTheQueueItFailedOnIsGone()
    {
        var queue = Queue("shard");
        await using var shard = await StartFailingSubscriberAsync(bus.RabbitMqConnection, queue);
        await FailAsync(bus.RabbitMqConnection, shard, queue, "p1");
        var failed = (await shard.ErrorQueue.ListAsync()).Value.Messages.Single();
        await BrokerQueues.DeleteQueueAsync(bus.RabbitMqConnection, queue);

        var retried = await shard.ErrorQueue.RetryAsync(failed.MessageId);

        retried.Errors.ShouldHaveSingleItem().Code.ShouldBe("ErrorQueue.NotMoved");
        (await BrokerQueues.MessageCountAsync(bus.RabbitMqConnection, $"{queue}.error")).ShouldBe(1);
    }

    private async Task<string> ShowsDeletesAndMovesBackAsync(RabbitMqConnection connection)
    {
        var queue = Queue("shard");
        await using var shard = await StartFailingSubscriberAsync(connection, queue);
        foreach (var player in new[] { "p1", "p2", "p3" })
            await FailAsync(connection, shard, queue, player);

        var listed = (await shard.ErrorQueue.ListAsync()).Value;
        listed.Count.ShouldBe(3);
        listed.Messages.Select(message => message.Text).ShouldAllBe(text => text!.Contains("\"fail\":true"));
        listed.Messages.Select(PlayerOf).ShouldBe(["p1", "p2", "p3"]);
        listed.Messages.ShouldAllBe(message => message.SourceQueue == queue && message.Error!.Contains("Discord.Unavailable"));
        (await shard.ErrorQueue.ListAsync()).Value.Messages.Select(PlayerOf).ShouldBe(["p1", "p2", "p3"]);   // listing keeps them

        (await shard.ErrorQueue.DeleteAsync(listed.Messages[1].MessageId)).IsSuccess.ShouldBeTrue();
        shard.Log.Recovered = true;
        (await shard.ErrorQueue.RetryAsync(listed.Messages[0].MessageId)).IsSuccess.ShouldBeTrue();
        await WaitUntilAsync(() => shard.Log.Handled.Any(renamed => renamed.PlayerId == "p1"));
        (await shard.ErrorQueue.RetryAllAsync()).Value.ShouldBe(1);
        await WaitUntilAsync(() => shard.Log.Handled.Any(renamed => renamed.PlayerId == "p3"));

        shard.Log.Handled.Select(renamed => renamed.PlayerId).Order(StringComparer.Ordinal).ShouldBe(["p1", "p3"]);
        (await BrokerQueues.MessageCountAsync(connection, $"{queue}.error")).ShouldBe(0);
        return $"{queue}.error";
    }

    private Task<EventService> StartFailingSubscriberAsync(RabbitMqConnection connection, string queue)
        => EventService.StartSubscriberAsync(connection, _wireName, queue, subscriber => subscriber.UseConfigurationStrategy(new OneTry()));

    /// <summary>Publishes an event that fails, and waits for it in the error queue.</summary>
    private async Task FailAsync(RabbitMqConnection connection, EventService shard, string queue, string player)
    {
        await using var publisher = await EventService.StartPublisherAsync(connection, _wireName);
        var before = await BrokerQueues.MessageCountAsync(connection, $"{queue}.error");
        await publisher.PublishAsync(new PlayerRenamedPublished(player, "Han", Fail: true));
        await WaitUntilAsync(async () => await BrokerQueues.MessageCountAsync(connection, $"{queue}.error") == Math.Max(before, 0) + 1);
        shard.Log.Handled.ShouldBeEmpty();
    }

    private static string PlayerOf(FailedMessage message)
        => message.Text!.Split("\"playerId\":\"")[1].Split('"')[0];

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

    /// <summary>One try, then the error queue.</summary>
    private sealed class OneTry : IBusConfigurationStrategy
    {
        public void ConfigureTransport(RabbitMqOptionsBuilder transport)
        {
        }

        public void ConfigureOptions(OptionsConfigurer options)
            => options.RetryStrategy(maxDeliveryAttempts: 1);
    }
}
