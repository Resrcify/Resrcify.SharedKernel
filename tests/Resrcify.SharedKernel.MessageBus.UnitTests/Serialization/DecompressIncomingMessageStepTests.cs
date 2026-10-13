using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using Rebus.Compression;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Transport;
using Resrcify.SharedKernel.MessageBus.Serialization;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Serialization;

/// <summary>Unzipping of a received body, whoever gzipped it.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class DecompressIncomingMessageStepTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(new string('x', 50_000));

    [Fact]
    public async Task Process_ShouldUnzipTheBodyAndDropTheHeader_WhenRebusCompressionZippedIt()
    {
        var received = Gzipped(new Zipper().Zip(Body));

        var handled = await ProcessAsync(received);

        handled.Body.ShouldBe(Body);
        handled.Headers.ShouldNotContainKey(Headers.ContentEncoding);
        handled.Headers[Headers.MessageId].ShouldBe("message-1");
    }

    [Fact]
    public async Task Process_ShouldUnzipTheBody_WhenThisPackageZippedIt()
    {
        var handled = await ProcessAsync(Gzipped(MessageCompression.Zip(Body)));

        handled.Body.ShouldBe(Body);
    }

    [Fact]
    public async Task Process_ShouldKeepTheReceivedMessageAsTheOriginal_WhenItUnzips()
    {
        var received = Gzipped(MessageCompression.Zip(Body));
        using var scope = new RebusTransactionScope();
        var context = new IncomingStepContext(received, scope.TransactionContext);

        await DecompressIncomingMessageStep.Instance.Process(context, () => Task.CompletedTask);

        context.Load<OriginalTransportMessage>().TransportMessage.ShouldBeSameAs(received);
    }

    [Fact]
    public async Task Process_ShouldLeaveTheMessage_WhenItIsNotCompressed()
    {
        var received = new TransportMessage(new Dictionary<string, string> { [Headers.MessageId] = "message-1" }, Body);

        var handled = await ProcessAsync(received);

        handled.ShouldBeSameAs(received);
    }

    [Fact]
    public async Task Process_ShouldLeaveAnotherEncodingToRebus()
    {
        var received = new TransportMessage(
            new Dictionary<string, string> { [Headers.ContentEncoding] = "br" },
            Body);

        var handled = await ProcessAsync(received);

        handled.ShouldBeSameAs(received);
    }

    private static async Task<TransportMessage> ProcessAsync(TransportMessage received)
    {
        using var scope = new RebusTransactionScope();
        var context = new IncomingStepContext(received, scope.TransactionContext);
        var nextCalled = false;

        await DecompressIncomingMessageStep.Instance.Process(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.ShouldBeTrue();
        return context.Load<TransportMessage>();
    }

    private static TransportMessage Gzipped(byte[] body)
        => new(
            new Dictionary<string, string>
            {
                [Headers.MessageId] = "message-1",
                [Headers.ContentEncoding] = "gzip",
            },
            body);
}
