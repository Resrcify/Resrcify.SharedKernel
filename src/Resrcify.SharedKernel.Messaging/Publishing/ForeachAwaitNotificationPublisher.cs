using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.Messaging.Publishing;

internal sealed class ForeachAwaitNotificationPublisher
    : INotificationPublisher
{
    public async Task Publish<TNotification>(
        IEnumerable<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : notnull
    {
        foreach (var handler in handlers)
            await handler.Handle(notification, cancellationToken);
    }
}
