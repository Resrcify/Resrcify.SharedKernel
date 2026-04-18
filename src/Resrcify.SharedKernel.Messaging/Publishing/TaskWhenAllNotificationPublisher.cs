using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.Messaging.Publishing;

internal sealed class TaskWhenAllNotificationPublisher
    : INotificationPublisher
{
    public Task Publish<TNotification>(
        IEnumerable<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : notnull
    {
        if (handlers is ICollection<INotificationHandler<TNotification>> collection)
        {
            var tasks = new Task[collection.Count];
            var index = 0;

            foreach (var handler in collection)
            {
                tasks[index] = handler.Handle(notification, cancellationToken);
                index++;
            }

            return Task.WhenAll(tasks);
        }

        var fallbackTasks = new List<Task>();

        foreach (var handler in handlers)
            fallbackTasks.Add(handler.Handle(notification, cancellationToken));

        return Task.WhenAll(fallbackTasks);
    }
}
