using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Publishing;
using Resrcify.SharedKernel.Mediator.Abstractions;

namespace Resrcify.SharedKernel.Mediator.Runtime;

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
