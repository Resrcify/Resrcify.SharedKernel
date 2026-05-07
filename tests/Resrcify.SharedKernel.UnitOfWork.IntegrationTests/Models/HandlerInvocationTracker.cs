using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;

internal sealed class HandlerInvocationTracker
{
    public ConcurrentBag<TestNameChangedEvent> Received { get; } = new();
}

internal sealed class TestNameChangedEventHandler(HandlerInvocationTracker tracker)
    : INotificationHandler<TestNameChangedEvent>
{
    public Task Handle(TestNameChangedEvent notification, CancellationToken cancellationToken)
    {
        tracker.Received.Add(notification);
        return Task.CompletedTask;
    }
}
