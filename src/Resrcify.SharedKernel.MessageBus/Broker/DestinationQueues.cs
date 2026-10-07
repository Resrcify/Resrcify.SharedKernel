using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.Broker;

/// <summary>Which of the queues a service sends to exist already, asked without declaring them (passively).</summary>
internal static class DestinationQueues
{
    private const ushort NotFound = 404;

    /// <summary>
    /// The queues among <paramref name="queues"/> that exist. When the broker can't be asked (unreachable), none: they
    /// are declared then, as they were before this check.
    /// </summary>
    public static HashSet<string> Existing(MessageBusSettings settings, IServiceProvider provider, IReadOnlyList<string> queues)
    {
        try
        {
            // Called while Rebus builds a bus, which is synchronous.
            return Task.Run(() => ExistingAsync(settings, provider, queues)).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is BrokerUnreachableException or OperationInterruptedException or TimeoutException)
        {
            return [];
        }
    }

    private static async Task<HashSet<string>> ExistingAsync(MessageBusSettings settings, IServiceProvider provider, IReadOnlyList<string> queues)
    {
        var factory = settings.CreateConnectionFactory(provider, "messagebus-destination-check");
        await using var connection = await factory.CreateConnectionAsync();
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var queue in queues)
        {
            // A channel per check: a missing queue closes the channel it was asked on.
            await using var channel = await connection.CreateChannelAsync();
            try
            {
                await channel.QueueDeclarePassiveAsync(queue);
                existing.Add(queue);
            }
            catch (OperationInterruptedException missing) when (missing.ShutdownReason?.ReplyCode == NotFound)
            {
                // Not there yet: declared by the bus.
            }
        }

        return existing;
    }
}
