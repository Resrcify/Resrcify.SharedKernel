using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Mediator.Runtime;

internal sealed partial class Mediator
{
    private static readonly MethodInfo PublishObjectDispatchMethod = GetRequiredStaticMethod(nameof(PublishObjectDispatch));
    private static readonly ConcurrentDictionary<Type, Func<Mediator, object, CancellationToken, Task>> PublishDispatchCache = new();

    private CachedRuntime? _lastPublishRuntime;

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

        // When the runtime type differs from the compile-time type (e.g. caller holds
        // a base-interface variable like IDomainEvent), dispatch via runtime type so
        // the correct INotificationHandler<TConcrete> instances are resolved. Without
        // this, GetServices<INotificationHandler<TBase>>() returns [] and the publish
        // silently no-ops.
        if (typeof(TNotification) != notification.GetType())
            return Publish((object)notification, cancellationToken);

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
        if (Volatile.Read(ref _lastPublishRuntime) is { } last && last.IsFor(typeof(TNotification), responseType: null))
            return (PublishRuntime<TNotification>)last.Runtime;

        var handlers = serviceProvider.GetServices<INotificationHandler<TNotification>>();
        var handlerArray = MaterializeServices(handlers);

        var runtime = new PublishRuntime<TNotification>(notificationPublisher, handlerArray);
        Volatile.Write(ref _lastPublishRuntime, new CachedRuntime(typeof(TNotification), ResponseType: null, runtime));
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
