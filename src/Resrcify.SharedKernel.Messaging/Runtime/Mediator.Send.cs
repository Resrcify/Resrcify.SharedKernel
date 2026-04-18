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
    private static readonly MethodInfo SendObjectDispatchMethod = GetRequiredStaticMethod(nameof(SendObjectDispatch));
    private static readonly MethodInfo SendTypedDispatchMethod = GetRequiredStaticMethod(nameof(SendTypedDispatch));
    private static readonly ConcurrentDictionary<Type, Func<Mediator, object, CancellationToken, Task<object?>>> SendDispatchCache = new();
    private static readonly ConcurrentDictionary<Type, Type> ClosedRequestInterfaceCache = new();

    private readonly ConcurrentDictionary<(Type RequestType, Type ResponseType), object> _sendRuntimeCache = new();
    private readonly ConcurrentDictionary<(Type RequestType, Type ResponseType), string> _sendRuntimeMissingCache = new();

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendTyped(request, cancellationToken);
    }

    public Task<object?> Send(
        object request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var dispatcher = SendDispatchCache.GetOrAdd(requestType, static type => CreateSendDispatcher(type));

        return dispatcher(this, request, cancellationToken);
    }

    private ValueTask<TResponse> SendInternal<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var runtime = GetOrCreateSendRuntime<TRequest, TResponse>();
        return runtime.Execute(request, cancellationToken);
    }

    private Task<TResponse> SendTyped<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken)
    {
        var requestType = request.GetType();
        var dispatcher = TypedSendDispatchCache<TResponse>.Cache.GetOrAdd(
            requestType,
            static type => CreateTypedSendDispatcher<TResponse>(type));

        return dispatcher(this, request, cancellationToken);
    }

    private ISendRuntimeExecutor<TRequest, TResponse> GetOrCreateSendRuntime<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
    {
        var key = (typeof(TRequest), typeof(TResponse));

        if (_sendRuntimeMissingCache.TryGetValue(key, out var cachedMissingMessage))
            throw new InvalidOperationException(cachedMissingMessage);

        if (_sendRuntimeCache.TryGetValue(key, out var cached))
            return (ISendRuntimeExecutor<TRequest, TResponse>)cached;

        var composedRuntime = GetComposedSendRuntime<TRequest, TResponse>(key);
        if (composedRuntime is not null)
        {
            _sendRuntimeCache.TryAdd(key, composedRuntime);
            return composedRuntime;
        }

        var preProcessors = serviceProvider.GetServices<IRequestPreProcessor<TRequest>>();
        var preProcessorArray = MaterializeServices(preProcessors);

        var postProcessors = serviceProvider.GetServices<IRequestPostProcessor<TRequest, TResponse>>();
        var postProcessorArray = MaterializeServices(postProcessors);

        var valueTaskHandler = serviceProvider.GetService<IValueTaskRequestHandler<TRequest, TResponse>>();
        if (valueTaskHandler is not null)
        {
            var valueTaskBehaviors = serviceProvider.GetServices<IValueTaskPipelineBehavior<TRequest, TResponse>>();
            var valueTaskBehaviorArray = MaterializeServices(valueTaskBehaviors);
            var valueTaskRequestBehaviors = serviceProvider.GetServices<IValueTaskRequestPipelineBehavior<TRequest, TResponse>>();
            var valueTaskRequestBehaviorArray = MaterializeServices(valueTaskRequestBehaviors);

            var valueTaskRuntime = new ValueTaskSendRuntime<TRequest, TResponse>(
                valueTaskHandler,
                valueTaskBehaviorArray,
                valueTaskRequestBehaviorArray,
                preProcessorArray,
                postProcessorArray);

            _sendRuntimeCache.TryAdd(key, valueTaskRuntime);
            return valueTaskRuntime;
        }

        var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
            ?? throw CreateAndCacheMissingSendHandlerException<TRequest, TResponse>(key);

        var behaviors = serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
        var behaviorArray = MaterializeServices(behaviors);
        var requestBehaviors = serviceProvider.GetServices<IRequestPipelineBehavior<TRequest, TResponse>>();
        var requestBehaviorArray = MaterializeServices(requestBehaviors);

        var runtime = new SendRuntime<TRequest, TResponse>(
            handler,
            behaviorArray,
            requestBehaviorArray,
            preProcessorArray,
            postProcessorArray);

        _sendRuntimeCache.TryAdd(key, runtime);
        return runtime;
    }

    private IDiComposedSendRuntime<TRequest, TResponse>? GetComposedSendRuntime<TRequest, TResponse>((Type RequestType, Type ResponseType) key)
        where TRequest : IRequest<TResponse>
    {
        try
        {
            return serviceProvider.GetService<IDiComposedSendRuntime<TRequest, TResponse>>();
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("No request handler registered for", StringComparison.Ordinal))
        {
            _sendRuntimeMissingCache.TryAdd(key, exception.Message);
            throw;
        }
    }

    private InvalidOperationException CreateAndCacheMissingSendHandlerException<TRequest, TResponse>((Type RequestType, Type ResponseType) key)
        where TRequest : IRequest<TResponse>
    {
        var message = $"No request handler registered for '{typeof(TRequest).FullName}'.";
        _sendRuntimeMissingCache.TryAdd(key, message);
        return new InvalidOperationException(message);
    }

    private static Type GetClosedRequestInterface(Type requestType)
    {
        if (requestType.IsInterface &&
            requestType.IsGenericType &&
            requestType.GetGenericTypeDefinition() == typeof(IRequest<>))
            return requestType;

        var interfaces = requestType.GetInterfaces();

        for (var index = 0; index < interfaces.Length; index++)
        {
            var implemented = interfaces[index];
            if (!implemented.IsGenericType)
                continue;

            if (implemented.GetGenericTypeDefinition() == typeof(IRequest<>))
                return implemented;
        }

        throw new InvalidOperationException($"Request type '{requestType.FullName}' does not implement IRequest<TResponse>.");
    }

    private static Func<Mediator, object, CancellationToken, Task<object?>> CreateSendDispatcher(Type requestType)
    {
        var requestInterface = ClosedRequestInterfaceCache.GetOrAdd(requestType, static type => GetClosedRequestInterface(type));
        var responseType = requestInterface.GetGenericArguments()[0];

        var closedDispatchMethod = SendObjectDispatchMethod.MakeGenericMethod(requestType, responseType);

        return closedDispatchMethod.CreateDelegate<Func<Mediator, object, CancellationToken, Task<object?>>>();
    }

    private static Func<Mediator, IRequest<TResponse>, CancellationToken, Task<TResponse>> CreateTypedSendDispatcher<TResponse>(Type requestType)
    {
        var closedDispatchMethod = SendTypedDispatchMethod.MakeGenericMethod(requestType, typeof(TResponse));
        return closedDispatchMethod.CreateDelegate<Func<Mediator, IRequest<TResponse>, CancellationToken, Task<TResponse>>>();
    }

    public static Task<object?> SendObjectDispatch<TRequest, TResponse>(
        Mediator mediator,
        object request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var response = mediator.SendInternal<TRequest, TResponse>((TRequest)request, cancellationToken);
        if (response.IsCompletedSuccessfully)
            return Task.FromResult<object?>(response.Result);

        return AwaitResponse(response);

        static async Task<object?> AwaitResponse(ValueTask<TResponse> pendingTask)
            => await pendingTask.ConfigureAwait(false);
    }

    public static Task<TResponse> SendTypedDispatch<TRequest, TResponse>(
        Mediator mediator,
        IRequest<TResponse> request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var runtime = mediator.GetOrCreateSendRuntime<TRequest, TResponse>();

        if (runtime is ISendTaskRuntimeExecutor<TRequest, TResponse> taskRuntime)
            return taskRuntime.ExecuteTask((TRequest)request, cancellationToken);

        return ConvertToTask(runtime.Execute((TRequest)request, cancellationToken));
    }

    private static class TypedSendDispatchCache<TResponse>
    {
        internal static readonly ConcurrentDictionary<Type, Func<Mediator, IRequest<TResponse>, CancellationToken, Task<TResponse>>> Cache = new();
    }
}
