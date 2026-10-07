using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Activation;
using Rebus.Compression;
using Rebus.Config;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Routing.TypeBased;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Resrcify.SharedKernel.MessageBus.UnitTests.Support;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Configuration;

/// <summary>What every bus gets: compression of large bodies.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageBusSettingsTests
{
    private static readonly string Large = new('x', 100_000);

    /// <summary>A container with nothing in it: the bus' clock falls back to the system clock.</summary>
    private static readonly IServiceProvider EmptyServices = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void OtherPublisherOf_ShouldReportEachClashingPublisherOnce_WhenItKeepsPublishing()
    {
        var settings = new MessageBusSettings();

        settings.OtherPublisherOf("PayoutRotated", "shard").ShouldBeNull();
        settings.OtherPublisherOf("PayoutRotated", "sentinel").ShouldBe("shard");
        settings.OtherPublisherOf("PayoutRotated", "sentinel").ShouldBeNull();   // warned every event before
        settings.OtherPublisherOf("PayoutRotated", "tournament").ShouldBe("shard");
        settings.OtherPublisherOf("PayoutRotated", "shard").ShouldBeNull();
    }

    [Fact]
    public async Task ConfigureEveryBus_ShouldCompressALargeMessage_AndNotASmallOne()
    {
        var network = new InMemNetwork();
        var queue = $"blobs-{Guid.NewGuid():N}";
        using var requester = await InMemoryServices.StartAsync(network, bus => bus.AddRequest<Blob, BlobEcho>(queue).AddScatterGather());
        var client = requester.Services.GetRequiredService<IScatterGatherClient>();

        // Nobody answers: the requests stay in the queue to look at.
        await client.GatherAsync<Blob, BlobEcho>(
            new Dictionary<string, Blob> { ["large"] = new(Large), ["small"] = new("small") },
            TimeSpan.FromMilliseconds(100));

        var messages = network.GetMessages(queue).ToDictionary(message => message.Headers[ScatterHeaders.ItemKey]);
        messages["large"].Headers[Headers.ContentEncoding].ShouldBe("gzip");
        messages["large"].Body.Length.ShouldBeLessThan(Large.Length / 10);
        messages["small"].Headers.ShouldNotContainKey(Headers.ContentEncoding);
    }

    [Fact]
    public async Task ConfigureEveryBus_ShouldDeliverALargeMessage_WhenBothEndsCompress()
    {
        var network = new InMemNetwork();
        using var responder = await InMemoryServices.StartAsync(network, bus => bus.AddRateLimitedQueue<Blob, BlobEcho, BlobEchoer>());
        using var requester = await InMemoryServices.StartAsync(network, bus => bus.AddRequest<Blob, BlobEcho>().AddScatterGather());

        var reply = await requester.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<Blob, BlobEcho>(new Blob(Large), TimeSpan.FromSeconds(5));

        reply.Value.Length.ShouldBe(Large.Length);
    }

    [Fact]
    public async Task ConfigureEveryBus_ShouldNotCompress_WhenCompressionIsOff()
    {
        var network = new InMemNetwork();
        var queue = $"blobs-{Guid.NewGuid():N}";
        using var requester = await InMemoryServices.StartAsync(
            network,
            bus => bus.CompressMessagesAbove(null).AddRequest<Blob, BlobEcho>(queue).AddScatterGather());

        await requester.Services.GetRequiredService<IScatterGatherClient>()
            .RequestAsync<Blob, BlobEcho>(new Blob(Large), TimeSpan.FromMilliseconds(100));

        network.GetMessages(queue).Single().Headers.ShouldNotContainKey(Headers.ContentEncoding);
    }

    [Fact]
    public async Task ConfigureEveryBus_ShouldUnzipAMessage_WhenRebusCompressionZippedIt()
    {
        var network = new InMemNetwork();
        var queue = $"blobs-{Guid.NewGuid():N}";
        var received = new TaskCompletionSource<(string Content, bool HeaderLeft)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var receiverActivator = new BuiltinHandlerActivator();
        receiverActivator.Handle<Blob>(blob =>
        {
            received.TrySetResult((blob.Content, MessageContext.Current.Headers.ContainsKey(Headers.ContentEncoding)));
            return Task.CompletedTask;
        });
        using var receiver = Configure.With(receiverActivator)
            .Logging(logging => logging.None())
            .Transport(transport => transport.UseInMemoryTransport(network, queue))
            .Options(options => new MessageBusSettings().ConfigureEveryBus(options, EmptyServices))
            .Start();
        using var senderActivator = new BuiltinHandlerActivator();
        using var sender = Configure.With(senderActivator)
            .Logging(logging => logging.None())
            .Transport(transport => transport.UseInMemoryTransportAsOneWayClient(network))
            .Routing(routing => routing.TypeBased().Map<Blob>(queue))
            .Options(options => options.EnableCompression(1024))
            .Start();

        await sender.Send(new Blob(Large));

        var (content, headerLeft) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        content.ShouldBe(Large);
        // Rebus' own unzipping leaves the header on the message being handled; this package's step removes it.
        headerLeft.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfigureEveryBus_ShouldZipWhatRebusUnzips_WhenTheReceiverIsPlainRebus()
    {
        var network = new InMemNetwork();
        var queue = $"blobs-{Guid.NewGuid():N}";
        var received = new TaskCompletionSource<(string Content, string? Encoding)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var receiverActivator = new BuiltinHandlerActivator();
        receiverActivator.Handle<Blob>(blob =>
        {
            MessageContext.Current.Headers.TryGetValue(Headers.ContentEncoding, out var encoding);
            received.TrySetResult((blob.Content, encoding));
            return Task.CompletedTask;
        });
        using var receiver = Configure.With(receiverActivator)
            .Logging(logging => logging.None())
            .Transport(transport => transport.UseInMemoryTransport(network, queue))
            .Start();
        using var senderActivator = new BuiltinHandlerActivator();
        using var sender = Configure.With(senderActivator)
            .Logging(logging => logging.None())
            .Transport(transport => transport.UseInMemoryTransportAsOneWayClient(network))
            .Routing(routing => routing.TypeBased().Map<Blob>(queue))
            .Options(options => new MessageBusSettings { CompressAboveBytes = 1024 }.ConfigureEveryBus(options, EmptyServices))
            .Start();

        await sender.Send(new Blob(Large));

        var (content, encoding) = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        content.ShouldBe(Large);
        encoding.ShouldBe("gzip");
    }

    internal sealed record Blob(string Content);

    internal sealed record BlobEcho(int Length);

    internal sealed class BlobEchoer : IRequestResponder<Blob, BlobEcho>
    {
        public Task<Result<BlobEcho>> HandleAsync(Blob request, CancellationToken cancellationToken = default)
            => Task.FromResult<Result<BlobEcho>>(new BlobEcho(request.Content.Length));
    }
}
