using System.Collections.Generic;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;

/// <summary>Reads queue state straight from RabbitMQ.</summary>
internal static class BrokerQueues
{
    /// <summary>
    /// A queue subscribed to <paramref name="wireName"/>'s topic that the broker keeps empty by refusing every message
    /// routed to it (<c>x-max-length</c> 0, <c>x-overflow</c> reject-publish): a publish of that event is nacked.
    /// </summary>
    public static async Task DeclareRefusingSubscriberAsync(RabbitMqConnection connection, string wireName, string queue)
    {
        var factory = new ConnectionFactory
        {
            HostName = connection.Host,
            Port = connection.Port,
            UserName = connection.Username,
            Password = connection.Password,
        };
        await using var rabbitMq = await factory.CreateConnectionAsync();
        await using var channel = await rabbitMq.CreateChannelAsync();
        await channel.ExchangeDeclareAsync("RebusTopics", ExchangeType.Topic, durable: true);
        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-max-length"] = 0, ["x-overflow"] = "reject-publish" });
        await channel.QueueBindAsync(queue, "RebusTopics", wireName);
    }

    /// <summary>How many messages wait in <paramref name="queue"/>, or -1 if it doesn't exist.</summary>
    public static async Task<long> MessageCountAsync(RabbitMqConnection connection, string queue)
    {
        var factory = new ConnectionFactory
        {
            HostName = connection.Host,
            Port = connection.Port,
            UserName = connection.Username,
            Password = connection.Password,
        };
        await using var rabbitMq = await factory.CreateConnectionAsync();
        await using var channel = await rabbitMq.CreateChannelAsync();
        try
        {
            return (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;
        }
        catch (OperationInterruptedException)
        {
            return -1;
        }
    }
}
