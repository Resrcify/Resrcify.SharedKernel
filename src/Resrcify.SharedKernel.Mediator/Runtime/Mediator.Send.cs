using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Abstractions;
using Resrcify.SharedKernel.Mediator.Diagnostics;

namespace Resrcify.SharedKernel.Mediator.Runtime;

internal sealed partial class Mediator
{
    private static readonly MethodInfo SendObjectDispatchMethod = GetRequiredStaticMethod(nameof(SendObjectDispatch));
    private static readonly MethodInfo SendTypedDispatchMethod = GetRequiredStaticMethod(nameof(SendTypedDispatch));
    private static readonly ConcurrentDictionary<Type, Func<Mediator, object, CancellationToken, Task<object?>>> SendDispatchCache = new();
    private static readonly ConcurrentDictionary<Type, Type> ClosedRequestInterfaceCache = new();

    private CachedRuntime? _lastSendRuntime;

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
        if (Volatile.Read(ref _lastSendRuntime) is { } last && last.IsFor(typeof(TRequest), typeof(TResponse)))
            return (ISendRuntimeExecutor<TRequest, TResponse>)last.Runtime;

        var runtime = CreateSendRuntime<TRequest, TResponse>();
        Volatile.Write(ref _lastSendRuntime, new CachedRuntime(typeof(TRequest), typeof(TResponse), runtime));
        return runtime;
    }

    private ISendRuntimeExecutor<TRequest, TResponse> CreateSendRuntime<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
    {
        var composedRuntime = GetComposedSendRuntime<TRequest, TResponse>();
        if (composedRuntime is not null)
            return composedRuntime;

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

            return new ValueTaskSendRuntime<TRequest, TResponse>(
                valueTaskHandler,
                valueTaskBehaviorArray,
                valueTaskRequestBehaviorArray,
                preProcessorArray,
                postProcessorArray);
        }

        var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()
            ?? throw new InvalidOperationException($"No request handler registered for '{typeof(TRequest).FullName}'.");

        var behaviors = serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
        var behaviorArray = MaterializeServices(behaviors);
        var requestBehaviors = serviceProvider.GetServices<IRequestPipelineBehavior<TRequest, TResponse>>();
        var requestBehaviorArray = MaterializeServices(requestBehaviors);

        return new SendRuntime<TRequest, TResponse>(
            handler,
            behaviorArray,
            requestBehaviorArray,
            preProcessorArray,
            postProcessorArray);
    }

    // The composed runtime doesn't throw when built without a handler (it reports that when executed).
    private IDiComposedSendRuntime<TRequest, TResponse>? GetComposedSendRuntime<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
        => serviceProvider.GetService<IDiComposedSendRuntime<TRequest, TResponse>>();

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
        if (MediatorDiagnostics.IsObserved)
            return ObservedAsObject(mediator.SendObservedAsync<TRequest, TResponse>((TRequest)request, cancellationToken));

        var response = mediator.SendInternal<TRequest, TResponse>((TRequest)request, cancellationToken);
        if (response.IsCompletedSuccessfully)
            return Task.FromResult<object?>(response.Result);

        return AwaitResponse(response);

        static async Task<object?> AwaitResponse(ValueTask<TResponse> pendingTask)
            => await pendingTask.ConfigureAwait(false);

        static async Task<object?> ObservedAsObject(Task<TResponse> observed)
            => await observed.ConfigureAwait(false);
    }

    public static Task<TResponse> SendTypedDispatch<TRequest, TResponse>(
        Mediator mediator,
        IRequest<TResponse> request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        if (MediatorDiagnostics.IsObserved)
            return mediator.SendObservedAsync<TRequest, TResponse>((TRequest)request, cancellationToken);

        var runtime = mediator.GetOrCreateSendRuntime<TRequest, TResponse>();

        if (runtime is ISendTaskRuntimeExecutor<TRequest, TResponse> taskRuntime)
            return taskRuntime.ExecuteTask((TRequest)request, cancellationToken);

        return ConvertToTask(runtime.Execute((TRequest)request, cancellationToken));
    }

    /// <summary>
    /// A send something listens to (see <see cref="MediatorDiagnostics"/>): in a span, timed on the
    /// <see cref="TimeProvider"/>, with its outcome recorded. An exception is recorded from a filter, which doesn't catch
    /// it: the caller gets it unchanged.
    /// </summary>
    private async Task<TResponse> SendObservedAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var time = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
        var started = time.GetTimestamp();
        using var activity = MediatorDiagnostics.StartSend(typeof(TRequest));
        try
        {
            var response = await SendInternal<TRequest, TResponse>(request, cancellationToken).ConfigureAwait(false);
            MediatorDiagnostics.RecordSent(activity, typeof(TRequest), response, time.GetElapsedTime(started));
            return response;
        }
        catch (Exception exception) when (RecordThrown(activity, typeof(TRequest), exception, time.GetElapsedTime(started)))
        {
            throw;   // never reached: the filter records and lets the exception go on unchanged
        }
    }

    private static bool RecordThrown(
        Activity? activity,
        Type requestType,
        Exception exception,
        TimeSpan elapsed)
    {
        MediatorDiagnostics.RecordThrown(activity, requestType, exception, elapsed);
        return false;
    }

    private static class TypedSendDispatchCache<TResponse>
    {
        internal static readonly ConcurrentDictionary<Type, Func<Mediator, IRequest<TResponse>, CancellationToken, Task<TResponse>>> Cache = new();
    }
}
