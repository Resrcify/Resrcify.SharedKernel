using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Messaging.Publishing;

namespace Resrcify.SharedKernel.Messaging.Runtime;

internal sealed partial class Mediator
{
    private sealed class PublishRuntime<TNotification>(
        INotificationPublisher publisher,
        INotificationHandler<TNotification>[] handlers)
        where TNotification : notnull
    {
        public Task Publish(TNotification notification, CancellationToken cancellationToken)
            => publisher.Publish(handlers, notification, cancellationToken);
    }
}
