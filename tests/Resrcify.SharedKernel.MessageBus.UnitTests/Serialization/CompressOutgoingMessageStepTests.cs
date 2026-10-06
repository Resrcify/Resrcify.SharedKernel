using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Rebus.Compression;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Pipeline.Send;
using Rebus.Transport;
using Resrcify.SharedKernel.MessageBus.Serialization;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Serialization;

/// <summary>Compression of a serialized body, with the threshold rule of Rebus' <c>EnableCompression</c>.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class CompressOutgoingMessageStepTests
{
    private const int Threshold = 1024;

    [Fact]
    public async Task Process_ShouldGzipTheBody_WhenItIsExactlyTheThreshold()
    {
        var body = Filled(Threshold);

        var sent = await ProcessAsync(new TransportMessage(MessageHeaders(), body));

        sent.Headers[Headers.ContentEncoding].ShouldBe("gzip");
        new Zipper().Unzip(sent.Body).ShouldBe(body);
    }

    [Fact]
    public async Task Process_ShouldLeaveTheBody_WhenItIsBelowTheThreshold()
    {
        var message = new TransportMessage(MessageHeaders(), Filled(Threshold - 1));

        var sent = await ProcessAsync(message);

        sent.ShouldBeSameAs(message);
        sent.Headers.ShouldNotContainKey(Headers.ContentEncoding);
    }

    [Fact]
    public async Task Process_ShouldKeepTheOtherHeaders_WhenItCompresses()
    {
        var sent = await ProcessAsync(new TransportMessage(MessageHeaders(), Filled(Threshold * 4)));

        sent.Headers[Headers.MessageId].ShouldBe("message-1");
    }

    [Fact]
    public async Task Process_ShouldLeaveTheBody_WhenItAlreadyHasAnEncoding()
    {
        var headers = MessageHeaders();
        headers[Headers.ContentEncoding] = "br";
        var message = new TransportMessage(headers, Filled(Threshold * 4));

        var sent = await ProcessAsync(message);

        sent.ShouldBeSameAs(message);
    }

    [Fact]
    public async Task Process_ShouldCallTheNextStep()
    {
        var called = false;
        using var scope = new RebusTransactionScope();
        var context = Context(new TransportMessage(MessageHeaders(), Filled(Threshold)), scope);

        await new CompressOutgoingMessageStep(Threshold).Process(context, () =>
        {
            called = true;
            return Task.CompletedTask;
        });

        called.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenTheThresholdIsNegative()
        => Should.Throw<ArgumentOutOfRangeException>(() => new CompressOutgoingMessageStep(-1));

    private static async Task<TransportMessage> ProcessAsync(TransportMessage message)
    {
        using var scope = new RebusTransactionScope();
        var context = Context(message, scope);

        await new CompressOutgoingMessageStep(Threshold).Process(context, () => Task.CompletedTask);

        return context.Load<TransportMessage>();
    }

    private static OutgoingStepContext Context(
        TransportMessage message,
        RebusTransactionScope scope)
    {
        var context = new OutgoingStepContext(
            new Message(MessageHeaders(), new object()),
            scope.TransactionContext,
            new DestinationAddresses(["queue"]));
        context.Save(message);
        return context;
    }

    private static Dictionary<string, string> MessageHeaders()
        => new() { [Headers.MessageId] = "message-1" };

    private static byte[] Filled(int length)
    {
        var body = new byte[length];
        Array.Fill(body, (byte)'x');
        return body;
    }
}
