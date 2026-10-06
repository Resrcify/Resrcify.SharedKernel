using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.Serialization;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Serialization;

/// <summary>
/// Messages go by their class name, so two services agree without naming anything: the requester's and the
/// responder's classes (or the publisher's and the subscriber's) only need the same name, not the same namespace.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class WireNameConventionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RequestAsync_ShouldReachTheResponder_WhenNeitherSideNamesTheMessages()
    {
        var network = new InMemNetwork();
        using var responder = await StartAsync(network, bus => bus
            .AddRateLimitedQueue<ResponderSide.Lookup, ResponderSide.LookupResult, LookupHandler>());
        using var requester = await StartAsync(network, bus => bus
            .AddRequest<RequesterSide.Lookup, RequesterSide.LookupResult>()
            .AddScatterGather());

        var reply = await requester.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<RequesterSide.Lookup, RequesterSide.LookupResult>(new RequesterSide.Lookup("han"), Timeout);

        reply.Value.ShouldBe(new RequesterSide.LookupResult("found han"));
    }

    [Fact]
    public async Task RequestAsync_ShouldReachTheResponder_WhenItsClassIsNamedAfterTheRequestersClass()
    {
        var network = new InMemNetwork();
        using var responder = await StartAsync(network, bus => bus
            .AddMessage<ResponderSide.LookupPayload>(nameof(RequesterSide.Lookup))
            .AddRateLimitedQueue<ResponderSide.LookupPayload, ResponderSide.LookupResult, LookupPayloadHandler>());
        using var requester = await StartAsync(network, bus => bus
            .AddRequest<RequesterSide.Lookup, RequesterSide.LookupResult>()
            .AddScatterGather());

        var reply = await requester.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<RequesterSide.Lookup, RequesterSide.LookupResult>(new RequesterSide.Lookup("leia"), Timeout);

        reply.Value.ShouldBe(new RequesterSide.LookupResult("payload leia"));
    }

    [Fact]
    public async Task PublishAsync_ShouldReachTheSubscriber_WhenNeitherSideNamesTheEvent()
    {
        var network = new InMemNetwork();
        var received = new ConcurrentQueue<SubscriberSide.LookupDone>();
        using var subscriber = await StartAsync(
            network,
            bus => bus.WithInputQueue("subscriber").AddEventHandlers(typeof(WireNameConventionTests).Assembly),
            services => services.AddSingleton(received));
        using var publisher = await StartAsync(network, _ => { });

        await publisher.Services.GetRequiredService<IEventBus>().PublishAsync(new PublisherSide.LookupDone("han"));

        var giveUpAt = DateTime.UtcNow + Timeout;
        while (received.IsEmpty && DateTime.UtcNow < giveUpAt)
            await Task.Delay(20);
        received.ShouldHaveSingleItem().ShouldBe(new SubscriberSide.LookupDone("han"));
    }

    [Fact]
    public void GetType_ShouldNameTheClash_WhenTwoReceivedTypesHaveTheSameName()
    {
        var settings = new MessageBusSettings();
        settings.Receive(typeof(RequesterSide.Lookup));
        settings.Receive(typeof(ResponderSide.Lookup));
        using var provider = new ServiceCollection().BuildServiceProvider();

        var clash = Should.Throw<InvalidOperationException>(() => new WireNameConvention(settings, provider).GetType("Lookup"));

        clash.Message.ShouldContain("'Lookup' names more than one message type");
    }

    [Fact]
    public void GetTypeName_ShouldBeTheClassNameWithoutTheNamespace_WhenTheTypeHasNoGivenName()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        new WireNameConvention(new MessageBusSettings(), provider).GetTypeName(typeof(RequesterSide.Lookup)).ShouldBe("Lookup");
    }

    private static async Task<IHost> StartAsync(
        InMemNetwork network,
        Action<MessageBusBuilder> configure,
        Action<IServiceCollection>? services = null)
    {
        var builder = Host.CreateApplicationBuilder();
        services?.Invoke(builder.Services);
        builder.Services.AddMessageBus(bus => configure(bus.UseInMemory(network)));
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    /// <summary>The requesting service's classes.</summary>
    internal static class RequesterSide
    {
        internal sealed record Lookup(string Name);

        internal sealed record LookupResult(string Text);
    }

    /// <summary>The responding service's classes: same names, another namespace (here: another enclosing class).</summary>
    internal static class ResponderSide
    {
        internal sealed record Lookup(string Name);

        internal sealed record LookupPayload(string Name);

        internal sealed record LookupResult(string Text);
    }

    internal static class PublisherSide
    {
        internal sealed record LookupDone(string Name);
    }

    internal static class SubscriberSide
    {
        internal sealed record LookupDone(string Name);
    }

    internal sealed class LookupHandler : IRequestResponder<ResponderSide.Lookup, ResponderSide.LookupResult>
    {
        public Task<Result<ResponderSide.LookupResult>> HandleAsync(ResponderSide.Lookup request, CancellationToken cancellationToken = default)
            => Task.FromResult<Result<ResponderSide.LookupResult>>(new ResponderSide.LookupResult("found " + request.Name));
    }

    internal sealed class LookupPayloadHandler : IRequestResponder<ResponderSide.LookupPayload, ResponderSide.LookupResult>
    {
        public Task<Result<ResponderSide.LookupResult>> HandleAsync(ResponderSide.LookupPayload request, CancellationToken cancellationToken = default)
            => Task.FromResult<Result<ResponderSide.LookupResult>>(new ResponderSide.LookupResult("payload " + request.Name));
    }

    internal sealed class LookupDoneHandler(ConcurrentQueue<SubscriberSide.LookupDone> received)
        : IIntegrationEventHandler<SubscriberSide.LookupDone>
    {
        public Task<Result> HandleAsync(SubscriberSide.LookupDone integrationEvent, CancellationToken cancellationToken)
        {
            received.Enqueue(integrationEvent);
            return Task.FromResult(Result.Success());
        }
    }
}
