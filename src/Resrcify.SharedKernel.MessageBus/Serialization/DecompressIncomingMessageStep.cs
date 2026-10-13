using System;
using System.Threading.Tasks;
using Rebus.Extensions;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;

namespace Resrcify.SharedKernel.MessageBus.Serialization;

/// <summary>
/// Unzips a gzipped body (<see cref="MessageCompression"/>) right before <see cref="DeserializeIncomingMessageStep"/>,
/// whoever compressed it: this package, or Rebus' <c>EnableCompression</c>. The decompressed message replaces the
/// received one for the steps after it, without the encoding header, so Rebus' own unzipping (on every bus) has
/// nothing left to do. The received message stays as Rebus' <see cref="OriginalTransportMessage"/>: a forwarded or
/// dead-lettered message goes on compressed. Another encoding is left to Rebus, which rejects it.
/// </summary>
[StepDocumentation("Unzips a body marked rbs2-content-encoding: gzip into a buffer sized from the gzip trailer.")]
internal sealed class DecompressIncomingMessageStep
    : IIncomingStep
{
    public static DecompressIncomingMessageStep Instance { get; } = new();

    public Task Process(
        IncomingStepContext context,
        Func<Task> next)
    {
        var message = context.Load<TransportMessage>();

        if (MessageCompression.IsGzipped(message.Headers))
            context.Save(Decompressed(message));

        return next();
    }

    private static TransportMessage Decompressed(
        TransportMessage message)
    {
        var headers = message.Headers.Clone();
        headers.Remove(Headers.ContentEncoding);

        return new TransportMessage(
            headers,
            MessageCompression.Unzip(message.Body));
    }
}
