using System;
using System.Collections.Generic;
using System.Linq;
using Rebus.Messages;
using Rebus.Pipeline;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>
/// The handlers that had finished with an event (handled it, or rejected it as its fault) when it went to the error
/// queue, carried in the <see cref="Header"/> header, so that moved back from there it resumes at the handler that
/// failed instead of running them all again.
/// </summary>
/// <remarks>
/// A handler is named by its type, namespace and generic arguments included but not its assembly's version, so a
/// deploy in between doesn't make it a stranger. Two handlers of the same type (two <c>ForwardEvent</c>s of one event)
/// are told apart by their order: the second is <c>&lt;type&gt;#2</c>.
/// </remarks>
internal static class HandledBy
{
    public const string Header = "resrcify-handled-by";

    private const char Separator = ';';

    /// <summary>Each handler's name, in their order.</summary>
    public static IReadOnlyList<string> KeysOf(IReadOnlyList<Type> handlerTypes)
    {
        var seen = new Dictionary<Type, int>();
        var keys = new List<string>(handlerTypes.Count);
        foreach (var type in handlerTypes)
        {
            var occurrence = seen[type] = seen.GetValueOrDefault(type) + 1;
            var name = NameOf(type);
            keys.Add(occurrence == 1 ? name : $"{name}#{occurrence}");
        }
        return keys;
    }

    /// <summary>The handlers the message being handled says had finished with it (none for a first delivery).</summary>
    public static HashSet<string> Read(IMessageContext? context)
    {
        var finished = new HashSet<string>(StringComparer.Ordinal);
        if (context?.Headers.TryGetValue(Header, out var value) == true)
            finished.UnionWith(value.Split(Separator, StringSplitOptions.RemoveEmptyEntries));
        return finished;
    }

    /// <summary>Records <paramref name="finished"/> on the message being handled, before it is dead-lettered.</summary>
    public static void Write(IMessageContext? context, IReadOnlySet<string> finished)
    {
        if (context is null || finished.Count == 0)
            return;

        var value = string.Join(Separator, finished.Order(StringComparer.Ordinal));
        context.TransportMessage.Headers[Header] = value;
        // The error queue gets the message as it was received (before it was unzipped, say).
        if (context.IncomingStepContext.Load<OriginalTransportMessage>() is { } original)
            original.TransportMessage.Headers[Header] = value;
    }

    private static string NameOf(Type type)
    {
        var name = type.IsNested && type.DeclaringType is { } outer
            ? $"{NameOf(outer)}+{BareName(type)}"
            : $"{type.Namespace}.{BareName(type)}";
        return type.IsGenericType
            ? $"{name}<{string.Join(',', type.GetGenericArguments().Select(NameOf))}>"
            : name;
    }

    private static string BareName(Type type)
        => type.Name.Split('`')[0];
}
