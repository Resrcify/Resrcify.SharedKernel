using System;
using System.Threading.Tasks;
using Rebus.Extensions;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Pipeline.Send;

namespace Resrcify.SharedKernel.MessageBus.Serialization;

/// <summary>
/// Gzips a serialized body of <see cref="ThresholdBytes"/> or more (<see cref="MessageCompression"/>), as Rebus'
/// <c>EnableCompression</c> does: same threshold rule, same header. It runs right after
/// <see cref="SerializeOutgoingMessageStep"/>, so what is sent, and measured, is the compressed body.
/// </summary>
[StepDocumentation("Gzips a body at or above the threshold at the fastest level, marking it with rbs2-content-encoding: gzip.")]
internal sealed class CompressOutgoingMessageStep(
    int thresholdBytes)
    : IOutgoingStep
{
    public int ThresholdBytes { get; } = thresholdBytes >= 0
        ? thresholdBytes
        : throw new ArgumentOutOfRangeException(nameof(thresholdBytes), thresholdBytes, "The threshold can't be negative.");

    public Task Process(
        OutgoingStepContext context,
        Func<Task> next)
    {
        var message = context.Load<TransportMessage>();

        if (ShouldCompress(message))
            context.Save(Compressed(message));

        return next();
    }

    // A body already carrying an encoding is left as it is: compressing it again would hide the first one.
    private bool ShouldCompress(
        TransportMessage message)
        => message.Body.Length >= ThresholdBytes
            && !message.Headers.ContainsKey(Headers.ContentEncoding);

    private static TransportMessage Compressed(
        TransportMessage message)
    {
        var headers = message.Headers.Clone();
        headers[Headers.ContentEncoding] = MessageCompression.Gzip;

        return new TransportMessage(
            headers,
            MessageCompression.Zip(message.Body));
    }
}
