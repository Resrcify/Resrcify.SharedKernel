using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Abstractions;

namespace Resrcify.SharedKernel.Mediator.Runtime;

internal sealed partial class Mediator
{
    private static readonly MethodInfo StreamTypedDispatchMethod = GetRequiredStaticMethod(nameof(StreamTypedDispatch));
    private CachedRuntime? _lastStreamRuntime;

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
        if (Volatile.Read(ref _lastStreamRuntime) is { } last && last.IsFor(typeof(TRequest), typeof(TResponse)))
            return (IStreamRuntimeExecutor<TRequest, TResponse>)last.Runtime;

        var runtime = CreateStreamRuntime<TRequest, TResponse>();
        Volatile.Write(ref _lastStreamRuntime, new CachedRuntime(typeof(TRequest), typeof(TResponse), runtime));
        return runtime;
    }

    private IStreamRuntimeExecutor<TRequest, TResponse> CreateStreamRuntime<TRequest, TResponse>()
        where TRequest : IStreamRequest<TResponse>
    {
        var composedRuntime = serviceProvider.GetService<IDiComposedStreamRuntime<TRequest, TResponse>>();
        if (composedRuntime is not null)
            return composedRuntime;

        var handler = serviceProvider.GetService<IStreamRequestHandler<TRequest, TResponse>>()
            ?? throw new InvalidOperationException($"No stream request handler registered for '{typeof(TRequest).FullName}'.");

        var behaviors = serviceProvider.GetServices<IStreamPipelineBehavior<TRequest, TResponse>>();
        var behaviorArray = MaterializeServices(behaviors);

        return new StreamRuntime<TRequest, TResponse>(handler, behaviorArray);
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
