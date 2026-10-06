using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;

/// <summary>Reads queue state straight from RabbitMQ.</summary>
internal static class BrokerQueues
{
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
