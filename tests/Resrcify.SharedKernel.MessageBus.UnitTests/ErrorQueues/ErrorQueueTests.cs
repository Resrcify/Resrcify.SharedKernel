using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Abstractions;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.ErrorQueues;

/// <summary>
/// The error queue on the in-memory network: an event whose second handler fails goes there after one try, and is
/// shown, deleted or moved back from there.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ErrorQueueTests
{
    [Fact]
    public async Task ListAsync_ShouldShowTheFailedEvent_WithWhereAndWhyItFailed()
    {
        await using var service = await FailingServiceAsync("e1");

        var listed = await service.ErrorQueue.ListAsync();

        listed.IsSuccess.ShouldBeTrue();
        listed.Value.Queue.ShouldBe(service.ErrorQueueName);
        listed.Value.Count.ShouldBe(1);
        var failed = listed.Value.Messages.ShouldHaveSingleItem();
        failed.MessageId.ShouldNotBeNullOrEmpty();
        failed.MessageType.ShouldBe(nameof(ScriptedEvent));
        failed.SourceQueue.ShouldBe(service.InputQueue);
        failed.Error!.ShouldContain(nameof(ScriptedEventSecondHandler));
        failed.Error!.ShouldContain("Discord.Down");
        failed.Text!.ShouldContain("e1");
        failed.SentOnUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task ListAsync_ShouldShowTheFirstMessages_AndKeepThemAll_InTheirOrder()
    {
        await using var service = await FailingServiceAsync("e1", "e2", "e3");

        var first = await service.ErrorQueue.ListAsync(max: 2);
        var all = await service.ErrorQueue.ListAsync();

        first.Value.Count.ShouldBe(3);
        first.Value.Messages.Select(message => message.Text).ShouldBe([Json("e1"), Json("e2")]);
        all.Value.Messages.Select(message => message.Text).ShouldBe([Json("e1"), Json("e2"), Json("e3")]);
    }

    [Fact]
    public async Task RetryAsync_ShouldHandleTheEventAgain_ResumingAtTheHandlerThatFailed()
    {
        await using var service = await FailingServiceAsync("e1");
        var failed = (await service.ErrorQueue.ListAsync()).Value.Messages.Single();

        var retried = await service.ErrorQueue.RetryAsync(failed.MessageId);

        retried.IsSuccess.ShouldBeTrue();
        await InMemoryServices.WaitUntilAsync(() => service.Log.Handled.Contains("e1"));
        service.Log.FirstRuns.ShouldBe(1);   // it had handled it before the second failed
        service.Network.GetCount(service.ErrorQueueName).ShouldBe(0);
    }

    [Fact]
    public async Task RetryAsync_ShouldPutTheEventBackInTheErrorQueue_WhenItFailsAgain()
    {
        await using var service = await FailingServiceAsync("e1");
        var failed = (await service.ErrorQueue.ListAsync()).Value.Messages.Single();
        service.Log.Answers.Enqueue(Result.Failure(Error.Failure("Discord.StillDown", "Still down.")));

        await service.ErrorQueue.RetryAsync(failed.MessageId);

        await InMemoryServices.WaitUntilAsync(() => service.Network.GetCount(service.ErrorQueueName) == 1);
        var again = (await service.ErrorQueue.ListAsync()).Value.Messages.Single();
        again.MessageId.ShouldBe(failed.MessageId);
        again.Error!.ShouldContain("Discord.StillDown");
        service.Log.FirstRuns.ShouldBe(1);
    }

    [Fact]
    public async Task RetryAllAsync_ShouldMoveEveryMessageBack_OrOnlyThoseOfTheType()
    {
        await using var service = await FailingServiceAsync("e1", "e2");

        var otherType = await service.ErrorQueue.RetryAllAsync("SomethingElse");
        var scripted = await service.ErrorQueue.RetryAllAsync(nameof(ScriptedEvent));

        otherType.Value.ShouldBe(0);
        scripted.Value.ShouldBe(2);
        await InMemoryServices.WaitUntilAsync(() => service.Log.Handled.Count == 2);
        service.Network.GetCount(service.ErrorQueueName).ShouldBe(0);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveThatMessageOnly()
    {
        await using var service = await FailingServiceAsync("e1", "e2");
        var first = (await service.ErrorQueue.ListAsync()).Value.Messages[0];

        var deleted = await service.ErrorQueue.DeleteAsync(first.MessageId);

        deleted.IsSuccess.ShouldBeTrue();
        var left = (await service.ErrorQueue.ListAsync()).Value;
        left.Count.ShouldBe(1);
        left.Messages.Single().Text.ShouldBe(Json("e2"));
    }

    [Fact]
    public async Task DeleteAllAsync_ShouldEmptyTheQueue()
    {
        await using var service = await FailingServiceAsync("e1", "e2");

        var deleted = await service.ErrorQueue.DeleteAllAsync();

        deleted.Value.ShouldBe(2);
        (await service.ErrorQueue.ListAsync()).Value.Count.ShouldBe(0);
        service.Log.Handled.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteAndRetry_ShouldAnswerNotFound_ForAnUnknownMessage()
    {
        await using var service = await FailingServiceAsync("e1");

        var deleted = await service.ErrorQueue.DeleteAsync("no-such-message");
        var retried = await service.ErrorQueue.RetryAsync("no-such-message");

        deleted.Errors.ShouldHaveSingleItem().Code.ShouldBe("ErrorQueue.MessageNotFound");
        retried.Errors.ShouldHaveSingleItem().Type.ShouldBe(ErrorType.NotFound);
        service.Network.GetCount(service.ErrorQueueName).ShouldBe(1);
    }

    [Fact]
    public async Task EveryOperation_ShouldAnswerNone_WhenTheServiceHasNoInputQueue()
    {
        using var host = await InMemoryServices.StartAsync(new InMemNetwork(), _ => { });
        var errorQueue = host.Services.GetRequiredService<IErrorQueue>();

        (await errorQueue.ListAsync()).Errors.ShouldHaveSingleItem().Code.ShouldBe("ErrorQueue.None");
        (await errorQueue.RetryAllAsync()).Errors.ShouldHaveSingleItem().Code.ShouldBe("ErrorQueue.None");
        (await errorQueue.DeleteAsync("id")).Errors.ShouldHaveSingleItem().Code.ShouldBe("ErrorQueue.None");
    }

    private static string Json(string id)
        => $$"""{"id":"{{id}}"}""";

    /// <summary>A service whose events go to the error queue after one try; one has failed for each id.</summary>
    private static async Task<FailingService> FailingServiceAsync(params string[] ids)
    {
        var network = new InMemNetwork();
        var log = new ScriptLog();
        var queue = $"subscriber-{Guid.NewGuid():N}";
        var subscriber = await InMemoryServices.StartAsync(
            network,
            bus => bus
                .WithInputQueue(queue)
                .AddEventHandlers(typeof(ErrorQueueTests).Assembly)
                .UseConfigurationStrategy(new OneTry()),
            services => services.AddSingleton(log));
        var publisher = await InMemoryServices.StartAsync(network, _ => { });
        var service = new FailingService(network, log, subscriber, publisher, queue);

        foreach (var id in ids)
        {
            log.Answers.Enqueue(Result.Failure(Error.Failure("Discord.Down", "Down.")));
            var before = network.GetCount(service.ErrorQueueName);
            await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new ScriptedEvent(id));
            await InMemoryServices.WaitUntilAsync(() => network.GetCount(service.ErrorQueueName) == before + 1);
        }
        return service;
    }

    private sealed class OneTry : IBusConfigurationStrategy
    {
        public void ConfigureTransport(RabbitMqOptionsBuilder transport)
        {
        }

        public void ConfigureOptions(OptionsConfigurer options)
            => options.RetryStrategy(maxDeliveryAttempts: 1);

        public void ConfigureConnectionFactory(ConnectionFactory factory)
        {
        }
    }

    private sealed record FailingService(
        InMemNetwork Network,
        ScriptLog Log,
        IHost Subscriber,
        IHost Publisher,
        string InputQueue)
        : IAsyncDisposable
    {
        public string ErrorQueueName => $"{InputQueue}.error";

        public IErrorQueue ErrorQueue => Subscriber.Services.GetRequiredService<IErrorQueue>();

        public async ValueTask DisposeAsync()
        {
            await Subscriber.StopAsync();
            await Publisher.StopAsync();
            Subscriber.Dispose();
            Publisher.Dispose();
        }
    }
}
