using System;
using Rebus.Serialization;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Diagnostics;

namespace Resrcify.SharedKernel.MessageBus.Serialization;

/// <summary>
/// Names messages on the wire by their class name (or the name they were given), not by namespace and assembly,
/// so the sender's and the receiver's classes only need the same name.
/// </summary>
internal sealed class WireNameConvention(MessageBusSettings settings, IServiceProvider provider)
    : IMessageTypeNameConvention
{
    public string GetTypeName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return settings.WireNameOf(type);
    }

    public Type GetType(string name)
    {
        if (settings.TypesByWireName(provider).TryGetValue(name, out var type))
            return type;
        MessageBusDiagnostics.RecordUnknown(name);
        throw new InvalidOperationException(
            $"No message type here is called '{name}'. A service receives what it answers (AddRateLimitedQueue), " +
            "the responses to what it asks (AddRequest), the events it handles (AddEventHandlers) and what it registers " +
            $"(AddMessage). If it no longer handles this event, unsubscribe with RemoveSubscription(\"{name}\").");
    }
}
