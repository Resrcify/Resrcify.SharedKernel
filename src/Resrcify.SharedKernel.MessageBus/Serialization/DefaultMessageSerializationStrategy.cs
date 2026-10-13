using System.Text.Json;
using Resrcify.SharedKernel.MessageBus.Abstractions;

namespace Resrcify.SharedKernel.MessageBus.Serialization;

/// <summary>Leaves the JSON on the package defaults (System.Text.Json web defaults).</summary>
public sealed class DefaultMessageSerializationStrategy : IMessageSerializationStrategy
{
    public static DefaultMessageSerializationStrategy Instance { get; } = new();

    public void Configure(JsonSerializerOptions options)
    {
        // The defaults are already applied; nothing to change.
    }
}
