using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Resrcify.SharedKernel.MessageBus.Abstractions;

namespace Resrcify.SharedKernel.MessageBus.Serialization;

/// <summary>
/// Checks, in a test, that two services' classes for one message agree on the wire: the sender's class written as
/// the bus writes it, and read as the receiver's class. Services keep their own copies of a message's class, so
/// nothing else notices when one copy drifts.
/// </summary>
/// <example>
/// <code>
/// [Fact]
/// public void ShardRankChanged_ShouldReadAsDiscordsCopy()
///     => MessageContract.Differences&lt;Shard.ShardRankChanged, Discord.ShardRankChanged&gt;(Sample).ShouldBeEmpty();
/// </code>
/// </example>
public static class MessageContract
{
    /// <summary>
    /// Writes <paramref name="sample"/> as the sender's bus does, reads it as <typeparamref name="TReceived"/>, writes
    /// that again, and lists every difference: a property the receiver drops, one it reads differently, and one it
    /// has that isn't sent (it gets its default). Empty when the receiver gets everything the sender sends.
    /// </summary>
    /// <param name="sample">A message with every property set, nested ones and lists included.</param>
    /// <param name="senderStrategy">The sender's serialization strategy, if it has one (e.g. one that leaves members out).</param>
    public static IReadOnlyList<string> Differences<TSent, TReceived>(
        TSent sample,
        IMessageSerializationStrategy? senderStrategy = null)
        where TSent : class
        where TReceived : class
    {
        ArgumentNullException.ThrowIfNull(sample);
        var senderOptions = new JsonSerializerOptions(MessageJson.Options);
        senderStrategy?.Configure(senderOptions);

        var sent = JsonSerializer.SerializeToNode(sample, senderOptions);
        var received = JsonSerializer.Deserialize<TReceived>(sent!.ToJsonString(), MessageJson.Options);
        var readBack = JsonSerializer.SerializeToNode(received, MessageJson.Options);

        var differences = new List<string>();
        Compare(sent, readBack, "$", typeof(TReceived).Name, differences);
        return differences;
    }

    private static void Compare(JsonNode? sent, JsonNode? received, string path, string receiver, List<string> differences)
    {
        switch (sent, received)
        {
            case (JsonObject sentObject, JsonObject receivedObject):
                CompareObjects(sentObject, receivedObject, path, receiver, differences);
                break;
            case (JsonArray sentArray, JsonArray receivedArray):
                if (sentArray.Count != receivedArray.Count)
                    differences.Add($"{path}: {sentArray.Count} items sent, {receivedArray.Count} read by {receiver}");
                for (var i = 0; i < Math.Min(sentArray.Count, receivedArray.Count); i++)
                    Compare(sentArray[i], receivedArray[i], $"{path}[{i}]", receiver, differences);
                break;
            default:
                if (!JsonNode.DeepEquals(sent, received))
                    differences.Add($"{path}: sent {Show(sent)}, read by {receiver} as {Show(received)}");
                break;
        }
    }

    private static void CompareObjects(JsonObject sent, JsonObject received, string path, string receiver, List<string> differences)
    {
        foreach (var (name, value) in sent)
        {
            if (received.TryGetPropertyValue(name, out var receivedValue))
                Compare(value, receivedValue, $"{path}.{name}", receiver, differences);
            else
                differences.Add($"{path}.{name}: sent, but {receiver} has no such property (dropped)");
        }
        foreach (var name in received.Select(property => property.Key).Where(name => !sent.ContainsKey(name)))
            differences.Add($"{path}.{name}: {receiver} has it, but it isn't sent (gets its default)");
    }

    private static string Show(JsonNode? node)
        => node?.ToJsonString() ?? "null";
}
