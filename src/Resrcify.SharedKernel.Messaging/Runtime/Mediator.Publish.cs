using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.Messaging.Runtime;

internal sealed partial class Mediator
{
    private static readonly MethodInfo PublishObjectDispatchMethod = GetRequiredStaticMethod(nameof(PublishObjectDispatch));
    private static readonly ConcurrentDictionary<Type, Func<Mediator, object, CancellationToken, Task>> PublishDispatchCache = new();

    private readonly ConcurrentDictionary<Type, object> _publishRuntimeCache = new();

    public Task Publish(
        object notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var notificationType = notification.GetType();
        var dispatcher = PublishDispatchCache.GetOrAdd(notificationType, static type => CreatePublishDispatcher(type));

        return dispatcher(this, notification, cancellationToken);
    }

    public Task Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : notnull
    {
        ArgumentNullException.ThrowIfNull(notification);
        return PublishTyped(notification, cancellationToken);
    }

    private Task PublishTyped<TNotification>(TNotification notification, CancellationToken cancellationToken)
        where TNotification : notnull
    {
        var runtime = GetOrCreatePublishRuntime<TNotification>();
        return runtime.Publish(notification, cancellationToken);
    }

    private PublishRuntime<TNotification> GetOrCreatePublishRuntime<TNotification>()
        where TNotification : notnull
    {
        var notificationType = typeof(TNotification);

        if (_publishRuntimeCache.TryGetValue(notificationType, out var cached))
            return (PublishRuntime<TNotification>)cached;

        var handlers = serviceProvider.GetServices<INotificationHandler<TNotification>>();
        var handlerArray = MaterializeServices(handlers);

        var runtime = new PublishRuntime<TNotification>(notificationPublisher, handlerArray);
        _publishRuntimeCache.TryAdd(notificationType, runtime);
        return runtime;
    }

    private static Func<Mediator, object, CancellationToken, Task> CreatePublishDispatcher(Type notificationType)
    {
        var closedDispatchMethod = PublishObjectDispatchMethod.MakeGenericMethod(notificationType);

        return closedDispatchMethod.CreateDelegate<Func<Mediator, object, CancellationToken, Task>>();
    }

    public static Task PublishObjectDispatch<TNotification>(
        Mediator mediator,
        object notification,
        CancellationToken cancellationToken)
        where TNotification : notnull
        => mediator.PublishTyped((TNotification)notification, cancellationToken);
}
