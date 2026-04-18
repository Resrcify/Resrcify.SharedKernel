using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.Messaging.Runtime;

internal sealed partial class Mediator
{
    private static readonly MethodInfo StreamTypedDispatchMethod = GetRequiredStaticMethod(nameof(StreamTypedDispatch));
    private readonly ConcurrentDictionary<(Type RequestType, Type ResponseType), object> _streamRuntimeCache = new();
    private readonly ConcurrentDictionary<(Type RequestType, Type ResponseType), string> _streamRuntimeMissingCache = new();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var dispatcher = TypedStreamDispatchCache<TResponse>.Cache.GetOrAdd(
            requestType,
            static type => CreateTypedStreamDispatcher<TResponse>(type));

        return dispatcher(this, request, cancellationToken);
    }

    private IStreamRuntimeExecutor<TRequest, TResponse> GetOrCreateStreamRuntime<TRequest, TResponse>()
        where TRequest : IStreamRequest<TResponse>
    {
        var key = (typeof(TRequest), typeof(TResponse));

        if (_streamRuntimeMissingCache.TryGetValue(key, out var cachedMissingMessage))
            throw new InvalidOperationException(cachedMissingMessage);

        if (_streamRuntimeCache.TryGetValue(key, out var cached))
            return (IStreamRuntimeExecutor<TRequest, TResponse>)cached;

        var composedRuntime = GetComposedStreamRuntime<TRequest, TResponse>(key);
        if (composedRuntime is not null)
        {
            _streamRuntimeCache.TryAdd(key, composedRuntime);
            return composedRuntime;
        }

        var handler = serviceProvider.GetService<IStreamRequestHandler<TRequest, TResponse>>()
            ?? throw CreateAndCacheMissingStreamHandlerException<TRequest, TResponse>(key);

        var behaviors = serviceProvider.GetServices<IStreamPipelineBehavior<TRequest, TResponse>>();
        var behaviorArray = MaterializeServices(behaviors);

        var runtime = new StreamRuntime<TRequest, TResponse>(handler, behaviorArray);

        _streamRuntimeCache.TryAdd(key, runtime);
        return runtime;
    }

    private IDiComposedStreamRuntime<TRequest, TResponse>? GetComposedStreamRuntime<TRequest, TResponse>((Type RequestType, Type ResponseType) key)
        where TRequest : IStreamRequest<TResponse>
    {
        try
        {
            return serviceProvider.GetService<IDiComposedStreamRuntime<TRequest, TResponse>>();
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("No stream request handler registered for", StringComparison.Ordinal))
        {
            _streamRuntimeMissingCache.TryAdd(key, exception.Message);
            throw;
        }
    }

    private InvalidOperationException CreateAndCacheMissingStreamHandlerException<TRequest, TResponse>((Type RequestType, Type ResponseType) key)
        where TRequest : IStreamRequest<TResponse>
    {
        var message = $"No stream request handler registered for '{typeof(TRequest).FullName}'.";
        _streamRuntimeMissingCache.TryAdd(key, message);
        return new InvalidOperationException(message);
    }

    private static Func<Mediator, IStreamRequest<TResponse>, CancellationToken, IAsyncEnumerable<TResponse>> CreateTypedStreamDispatcher<TResponse>(Type requestType)
    {
        var closedDispatchMethod = StreamTypedDispatchMethod.MakeGenericMethod(requestType, typeof(TResponse));
        return closedDispatchMethod.CreateDelegate<Func<Mediator, IStreamRequest<TResponse>, CancellationToken, IAsyncEnumerable<TResponse>>>();
    }

    public static IAsyncEnumerable<TResponse> StreamTypedDispatch<TRequest, TResponse>(
        Mediator mediator,
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken)
        where TRequest : IStreamRequest<TResponse>
        => mediator.GetOrCreateStreamRuntime<TRequest, TResponse>().Create((TRequest)request, cancellationToken);

    private static class TypedStreamDispatchCache<TResponse>
    {
        internal static readonly ConcurrentDictionary<Type, Func<Mediator, IStreamRequest<TResponse>, CancellationToken, IAsyncEnumerable<TResponse>>> Cache = new();
    }
}
