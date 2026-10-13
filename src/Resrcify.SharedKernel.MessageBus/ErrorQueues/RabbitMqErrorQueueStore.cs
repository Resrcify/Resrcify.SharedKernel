using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using RebusHeaders = Rebus.Messages.Headers;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.ErrorQueues;

/// <summary>
/// The error queue on RabbitMQ, read with <c>basic.get</c> on a connection of its own (the service's connection and
/// configuration strategy, as the broker watcher's).
/// </summary>
/// <remarks>
/// A scan holds every message it reads (unacknowledged) until it is done, so it reads each once, then hands the kept
/// ones back with a requeueing <c>basic.nack</c>: a message returned by a closing channel instead counts as a delivery,
/// which a quorum queue limits. A quorum queue takes them back asynchronously, so the scan waits until they are back
/// (at most <see cref="SettleTimeout"/>): otherwise an operation right after it would miss them. A message moved back
/// is published to its queue through the default exchange,
/// mandatory and confirmed, before it is removed from the error queue: one whose queue is gone (returned) or that the
/// broker refuses stays.
/// </remarks>
internal sealed class RabbitMqErrorQueueStore(
    MessageBusSettings settings,
    IServiceProvider provider)
    : IErrorQueueStore
{
    private const ushort NotFound = 404;

    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan SettlePoll = TimeSpan.FromMilliseconds(20);

    public async Task<int> CountAsync(
        string queue,
        CancellationToken cancellationToken)
    {
        await using var connection = await ConnectAsync(cancellationToken);
        return await MessageCountAsync(connection, queue, cancellationToken) ?? 0;
    }

    public async Task<IReadOnlyList<ScanResult>> ScanAsync(
        string queue,
        int limit,
        Func<StoredMessage, ScanStep> decide,
        CancellationToken cancellationToken)
    {
        await using var connection = await ConnectAsync(cancellationToken);
        if (await MessageCountAsync(connection, queue, cancellationToken) is not { } count)
            return [];

        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        var results = new List<ScanResult>();
        ulong? lastKept = null;
        var keptCount = 0;
        try
        {
            for (var read = 0; read < Math.Min(count, limit); read++)
            {
                var delivery = await channel.BasicGetAsync(queue, autoAck: false, cancellationToken);
                if (delivery is null)
                    break;

                var message = new StoredMessage(HeadersOf(delivery.BasicProperties), delivery.Body.ToArray());
                var step = decide(message);
                if (step.Action == ScanAction.Keep)
                {
                    lastKept = delivery.DeliveryTag;
                    keptCount++;
                    continue;
                }

                if (step.Action == ScanAction.MoveBack
                    && await PublishAsync(channel, step.Destination!, delivery.BasicProperties, message.Body, cancellationToken) is { } failure)
                {
                    lastKept = delivery.DeliveryTag;
                    keptCount++;
                    results.Add(new ScanResult(message, step.Action, failure));
                    continue;
                }

                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                results.Add(new ScanResult(message, step.Action));
            }
        }
        finally
        {
            // Every message kept is unacknowledged up to the last one, and only those: the others were acknowledged.
            if (lastKept is { } tag && channel.IsOpen)
            {
                await channel.BasicNackAsync(tag, multiple: true, requeue: true, CancellationToken.None);
                await WaitUntilBackAsync(connection, queue, keptCount);
            }
        }

        return results;
    }

    /// <summary>Waits until the queue holds at least the <paramref name="kept"/> messages handed back.</summary>
    private static async Task WaitUntilBackAsync(IConnection connection, string queue, int kept)
    {
        var giveUpAt = DateTime.UtcNow + SettleTimeout;
        while (DateTime.UtcNow < giveUpAt
            && await MessageCountAsync(connection, queue, CancellationToken.None) is { } ready
            && ready < kept)
            await Task.Delay(SettlePoll, CancellationToken.None);
    }

    private async Task<IConnection> ConnectAsync(CancellationToken cancellationToken)
        => await settings
            .CreateConnectionFactory(provider, "messagebus-error-queue")
            .CreateConnectionAsync(cancellationToken);

    // Asked passively, on a channel of its own: a missing queue closes the channel it was asked on.
    private static async Task<int?> MessageCountAsync(
        IConnection connection,
        string queue,
        CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        try
        {
            var declared = await channel.QueueDeclarePassiveAsync(queue, cancellationToken);
            return (int)Math.Min(declared.MessageCount, int.MaxValue);
        }
        catch (OperationInterruptedException missing) when (missing.ShutdownReason?.ReplyCode == NotFound)
        {
            return null;
        }
    }

    /// <summary>Publishes a copy without its error headers; why it failed, or <see langword="null"/>.</summary>
    private static async Task<string?> PublishAsync(
        IChannel channel,
        string destination,
        IReadOnlyBasicProperties original,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        var properties = new BasicProperties(original)
        {
            Headers = original.Headers?
                .Where(header => !StoredMessageHeaders.IsErrorHeader(header.Key))
                .ToDictionary(header => header.Key, header => header.Value, StringComparer.Ordinal),
        };
        try
        {
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: destination,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
            return null;
        }
        catch (PublishException refused)
        {
            return refused.IsReturn
                ? $"queue {destination} doesn't exist"
                : $"the broker refused it for queue {destination}";
        }
    }

    /// <summary>
    /// The Rebus headers, as Rebus' RabbitMQ transport reads them: the AMQP headers (strings as UTF-8 bytes), with the
    /// ID, type and content headers taken from the message's properties where the headers lack them.
    /// </summary>
    private static Dictionary<string, string> HeadersOf(IReadOnlyBasicProperties properties)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in properties.Headers ?? new Dictionary<string, object?>())
            headers[name] = value switch
            {
                null => string.Empty,
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            };

        AddIfMissing(headers, RebusHeaders.MessageId, properties.MessageId);
        AddIfMissing(headers, RebusHeaders.Type, properties.Type);
        AddIfMissing(headers, RebusHeaders.ContentType, properties.ContentType);
        AddIfMissing(headers, RebusHeaders.ContentEncoding, properties.ContentEncoding);
        return headers;
    }

    private static void AddIfMissing(Dictionary<string, string> headers, string name, string? value)
    {
        if (value is not null)
            headers.TryAdd(name, value);
    }
}
