using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Messaging.Publishing;

namespace Resrcify.SharedKernel.Messaging.Runtime;

internal sealed partial class Mediator(
    IServiceProvider serviceProvider,
    INotificationPublisher notificationPublisher)
    : IMediator
{
    private static MethodInfo GetRequiredStaticMethod(string methodName)
        => typeof(Mediator).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Static method '{methodName}' was not found on mediator runtime.");

    private static TService[] MaterializeServices<TService>(IEnumerable<TService> services)
    {
        if (services is TService[] array)
            return array;

        if (services is ICollection<TService> collection)
        {
            if (collection.Count == 0)
                return [];

            if (collection.Count == 1)
            {
                if (collection is IList<TService> list)
                    return [list[0]];

                using var singleItemEnumerator = collection.GetEnumerator();
                _ = singleItemEnumerator.MoveNext();
                return [singleItemEnumerator.Current];
            }

            var copied = new TService[collection.Count];
            collection.CopyTo(copied, 0);
            return copied;
        }

        using var enumerator = services.GetEnumerator();
        if (!enumerator.MoveNext())
            return [];

        var first = enumerator.Current;
        if (!enumerator.MoveNext())
            return [first];

        var items = new List<TService>
        {
            first,
            enumerator.Current
        };

        while (enumerator.MoveNext())
            items.Add(enumerator.Current);

        return [.. items];
    }

    private static Task<TResponse> ConvertToTask<TResponse>(ValueTask<TResponse> response)
    {
        if (response.IsCompletedSuccessfully)
            return Task.FromResult(response.Result);

        return response.AsTask();
    }
}
