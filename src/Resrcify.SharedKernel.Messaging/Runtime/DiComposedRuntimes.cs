using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.Messaging.Runtime;

internal interface ISendRuntimeExecutor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> Execute(TRequest request, CancellationToken cancellationToken);
}

internal interface ISendTaskRuntimeExecutor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> ExecuteTask(TRequest request, CancellationToken cancellationToken);
}

internal interface IStreamRuntimeExecutor<TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Create(TRequest request, CancellationToken cancellationToken);
}

internal interface IDiComposedSendRuntime<TRequest, TResponse> : ISendRuntimeExecutor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>;

internal interface IDiComposedStreamRuntime<TRequest, TResponse> : IStreamRuntimeExecutor<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>;

internal sealed class DiComposedSendRuntime<TRequest, TResponse>(IServiceProvider serviceProvider)
    : IDiComposedSendRuntime<TRequest, TResponse>, ISendTaskRuntimeExecutor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IRequestPreProcessor<TRequest>[] _preProcessors = MaterializeServices(serviceProvider.GetServices<IRequestPreProcessor<TRequest>>());
    private readonly IRequestPostProcessor<TRequest, TResponse>[] _postProcessors = MaterializeServices(serviceProvider.GetServices<IRequestPostProcessor<TRequest, TResponse>>());
    private readonly IRequestHandler<TRequest, TResponse>? _taskHandler = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>();
    private readonly IValueTaskRequestHandler<TRequest, TResponse>? _valueTaskHandler = serviceProvider.GetService<IValueTaskRequestHandler<TRequest, TResponse>>();
    private readonly RequestExecutionDelegate<TRequest, TResponse>? _taskExecutor = serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>() is null
        ? null
        : BuildTaskExecutor(
            serviceProvider.GetService<IRequestHandler<TRequest, TResponse>>()!,
            MaterializeServices(serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>()),
            MaterializeServices(serviceProvider.GetServices<IRequestPipelineBehavior<TRequest, TResponse>>()));
    private readonly ValueTaskRequestExecutionDelegate<TRequest, TResponse>? _valueTaskExecutor = serviceProvider.GetService<IValueTaskRequestHandler<TRequest, TResponse>>() is null
        ? null
        : BuildValueTaskExecutor(
            serviceProvider.GetService<IValueTaskRequestHandler<TRequest, TResponse>>()!,
            MaterializeServices(serviceProvider.GetServices<IValueTaskPipelineBehavior<TRequest, TResponse>>()),
            MaterializeServices(serviceProvider.GetServices<IValueTaskRequestPipelineBehavior<TRequest, TResponse>>()));

    public ValueTask<TResponse> Execute(TRequest request, CancellationToken cancellationToken)
    {
        if (_valueTaskHandler is not null)
            return ExecuteValueTask(request, cancellationToken);

        if (_taskHandler is null)
            throw new InvalidOperationException($"No request handler registered for '{typeof(TRequest).FullName}'.");

        return ExecuteTaskCore(request, cancellationToken);
    }

    public Task<TResponse> ExecuteTask(TRequest request, CancellationToken cancellationToken)
    {
        if (_taskHandler is null)
            return Execute(request, cancellationToken).AsTask();

        if (_preProcessors.Length == 0 && _postProcessors.Length == 0)
            return _taskExecutor!(request, cancellationToken);

        return Execute(request, cancellationToken).AsTask();
    }

    private ValueTask<TResponse> ExecuteTaskCore(TRequest request, CancellationToken cancellationToken)
    {
        var executor = _taskExecutor!;

        if (_preProcessors.Length == 0 && _postProcessors.Length == 0)
            return new ValueTask<TResponse>(executor(request, cancellationToken));

        for (var index = 0; index < _preProcessors.Length; index++)
        {
            var preProcessTask = _preProcessors[index].Process(request, cancellationToken);
            if (!preProcessTask.IsCompletedSuccessfully)
                return new ValueTask<TResponse>(ExecuteSlowTask(request, index, preProcessTask, cancellationToken));
        }

        var responseTask = executor(request, cancellationToken);
        if (!responseTask.IsCompletedSuccessfully)
            return new ValueTask<TResponse>(ExecuteSlowTask(request, _preProcessors.Length, responseTask, cancellationToken));

        if (_postProcessors.Length == 0)
            return new ValueTask<TResponse>(responseTask);

        var response = responseTask.GetAwaiter().GetResult();

        for (var index = 0; index < _postProcessors.Length; index++)
        {
            var postProcessTask = _postProcessors[index].Process(request, response, cancellationToken);
            if (!postProcessTask.IsCompletedSuccessfully)
                return new ValueTask<TResponse>(ExecuteSlowTask(request, response, index, postProcessTask, cancellationToken));
        }

        return ValueTask.FromResult(response);
    }

    private ValueTask<TResponse> ExecuteValueTask(TRequest request, CancellationToken cancellationToken)
    {
        var executor = _valueTaskExecutor!;

        if (_preProcessors.Length == 0 && _postProcessors.Length == 0)
            return executor(request, cancellationToken);

        for (var index = 0; index < _preProcessors.Length; index++)
        {
            var preProcessTask = _preProcessors[index].Process(request, cancellationToken);
            if (!preProcessTask.IsCompletedSuccessfully)
                return ExecuteSlowValueTask(request, index, preProcessTask, cancellationToken);
        }

        var response = executor(request, cancellationToken);
        if (!response.IsCompletedSuccessfully)
            return ExecuteSlowValueTask(request, _preProcessors.Length, response, cancellationToken);

        if (_postProcessors.Length == 0)
            return response;

        var result = response.Result;

        for (var index = 0; index < _postProcessors.Length; index++)
        {
            var postProcessTask = _postProcessors[index].Process(request, result, cancellationToken);
            if (!postProcessTask.IsCompletedSuccessfully)
                return ExecuteSlowValueTask(request, result, index, postProcessTask, cancellationToken);
        }

        return ValueTask.FromResult(result);
    }

    private async Task<TResponse> ExecuteSlowTask(
        TRequest request,
        int preProcessorIndex,
        Task pendingPreProcessorTask,
        CancellationToken cancellationToken)
    {
        await pendingPreProcessorTask.ConfigureAwait(false);

        for (var index = preProcessorIndex + 1; index < _preProcessors.Length; index++)
            await _preProcessors[index].Process(request, cancellationToken).ConfigureAwait(false);

        var response = await _taskExecutor!(request, cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < _postProcessors.Length; index++)
            await _postProcessors[index].Process(request, response, cancellationToken).ConfigureAwait(false);

        return response;
    }

    private async Task<TResponse> ExecuteSlowTask(
        TRequest request,
        int preProcessorCount,
        Task<TResponse> responseTask,
        CancellationToken cancellationToken)
    {
        for (var index = preProcessorCount; index < _preProcessors.Length; index++)
            await _preProcessors[index].Process(request, cancellationToken).ConfigureAwait(false);

        var response = await responseTask.ConfigureAwait(false);

        for (var index = 0; index < _postProcessors.Length; index++)
            await _postProcessors[index].Process(request, response, cancellationToken).ConfigureAwait(false);

        return response;
    }

    private async Task<TResponse> ExecuteSlowTask(
        TRequest request,
        TResponse response,
        int postProcessorIndex,
        Task pendingPostProcessorTask,
        CancellationToken cancellationToken)
    {
        await pendingPostProcessorTask.ConfigureAwait(false);

        for (var index = postProcessorIndex + 1; index < _postProcessors.Length; index++)
            await _postProcessors[index].Process(request, response, cancellationToken).ConfigureAwait(false);

        return response;
    }

    private async ValueTask<TResponse> ExecuteSlowValueTask(
        TRequest request,
        int preProcessorIndex,
        Task pendingPreProcessorTask,
        CancellationToken cancellationToken)
    {
        await pendingPreProcessorTask.ConfigureAwait(false);

        for (var index = preProcessorIndex + 1; index < _preProcessors.Length; index++)
            await _preProcessors[index].Process(request, cancellationToken).ConfigureAwait(false);

        var response = await _valueTaskExecutor!(request, cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < _postProcessors.Length; index++)
            await _postProcessors[index].Process(request, response, cancellationToken).ConfigureAwait(false);

        return response;
    }

    private async ValueTask<TResponse> ExecuteSlowValueTask(
        TRequest request,
        int preProcessorCount,
        ValueTask<TResponse> response,
        CancellationToken cancellationToken)
    {
        for (var index = preProcessorCount; index < _preProcessors.Length; index++)
            await _preProcessors[index].Process(request, cancellationToken).ConfigureAwait(false);

        var result = await response.ConfigureAwait(false);

        for (var index = 0; index < _postProcessors.Length; index++)
            await _postProcessors[index].Process(request, result, cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async ValueTask<TResponse> ExecuteSlowValueTask(
        TRequest request,
        TResponse response,
        int postProcessorIndex,
        Task pendingPostProcessorTask,
        CancellationToken cancellationToken)
    {
        await pendingPostProcessorTask.ConfigureAwait(false);

        for (var index = postProcessorIndex + 1; index < _postProcessors.Length; index++)
            await _postProcessors[index].Process(request, response, cancellationToken).ConfigureAwait(false);

        return response;
    }

    private static RequestExecutionDelegate<TRequest, TResponse> BuildTaskExecutor(
        IRequestHandler<TRequest, TResponse> requestHandler,
        IPipelineBehavior<TRequest, TResponse>[] pipelineBehaviors,
        IRequestPipelineBehavior<TRequest, TResponse>[] requestPipelineBehaviors)
    {
        RequestExecutionDelegate<TRequest, TResponse> execute = (request, cancellationToken) =>
            requestHandler.Handle(request, cancellationToken);

        for (var index = requestPipelineBehaviors.Length - 1; index >= 0; index--)
        {
            var behavior = requestPipelineBehaviors[index];
            var current = execute;
            execute = (request, cancellationToken) =>
                behavior.Handle(
                    request,
                    current,
                    cancellationToken);
        }

        for (var index = pipelineBehaviors.Length - 1; index >= 0; index--)
        {
            var behavior = pipelineBehaviors[index];
            var current = execute;
            execute = (request, cancellationToken) =>
                behavior.Handle(
                    request,
                    nextToken => current(request, nextToken),
                    cancellationToken);
        }

        return execute;
    }

    private static ValueTaskRequestExecutionDelegate<TRequest, TResponse> BuildValueTaskExecutor(
        IValueTaskRequestHandler<TRequest, TResponse> requestHandler,
        IValueTaskPipelineBehavior<TRequest, TResponse>[] pipelineBehaviors,
        IValueTaskRequestPipelineBehavior<TRequest, TResponse>[] requestPipelineBehaviors)
    {
        ValueTaskRequestExecutionDelegate<TRequest, TResponse> execute = (request, cancellationToken) =>
            requestHandler.Handle(request, cancellationToken);

        for (var index = requestPipelineBehaviors.Length - 1; index >= 0; index--)
        {
            var behavior = requestPipelineBehaviors[index];
            var current = execute;
            execute = (request, cancellationToken) =>
                behavior.Handle(
                    request,
                    current,
                    cancellationToken);
        }

        for (var index = pipelineBehaviors.Length - 1; index >= 0; index--)
        {
            var behavior = pipelineBehaviors[index];
            var current = execute;
            execute = (request, cancellationToken) =>
                behavior.Handle(
                    request,
                    nextToken => current(request, nextToken),
                    cancellationToken);
        }

        return execute;
    }

    private static TService[] MaterializeServices<TService>(IEnumerable<TService> services)
    {
        if (services is TService[] array)
            return array;

        if (services is ICollection<TService> collection)
        {
            if (collection.Count == 0)
                return [];

            if (collection.Count == 1)
            {
                if (collection is IList<TService> list)
                    return [list[0]];

                using var singleItemEnumerator = collection.GetEnumerator();
                _ = singleItemEnumerator.MoveNext();
                return [singleItemEnumerator.Current];
            }

            var copied = new TService[collection.Count];
            collection.CopyTo(copied, 0);
            return copied;
        }

        using var enumerator = services.GetEnumerator();
        if (!enumerator.MoveNext())
            return [];

        var first = enumerator.Current;
        if (!enumerator.MoveNext())
            return [first];

        var items = new List<TService>
        {
            first,
            enumerator.Current
        };

        while (enumerator.MoveNext())
            items.Add(enumerator.Current);

        return [.. items];
    }
}

internal sealed class DiComposedStreamRuntime<TRequest, TResponse>(IServiceProvider serviceProvider)
    : IDiComposedStreamRuntime<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    private readonly StreamExecutor _executor = BuildExecutor(
        serviceProvider.GetService<IStreamRequestHandler<TRequest, TResponse>>()
            ?? throw new InvalidOperationException($"No stream request handler registered for '{typeof(TRequest).FullName}'."),
        MaterializeServices(serviceProvider.GetServices<IStreamPipelineBehavior<TRequest, TResponse>>()));

    public IAsyncEnumerable<TResponse> Create(TRequest request, CancellationToken cancellationToken)
        => _executor(request, cancellationToken);

    private delegate IAsyncEnumerable<TResponse> StreamExecutor(TRequest request, CancellationToken cancellationToken);

    private static StreamExecutor BuildExecutor(
        IStreamRequestHandler<TRequest, TResponse> streamHandler,
        IStreamPipelineBehavior<TRequest, TResponse>[] pipelineBehaviors)
    {
        StreamExecutor execute = (request, cancellationToken) =>
            streamHandler.Handle(request, cancellationToken);

        for (var index = pipelineBehaviors.Length - 1; index >= 0; index--)
        {
            var behavior = pipelineBehaviors[index];
            var current = execute;
            execute = (request, cancellationToken) =>
                behavior.Handle(
                    request,
                    nextToken => current(request, nextToken),
                    cancellationToken);
        }

        return execute;
    }

    private static TService[] MaterializeServices<TService>(IEnumerable<TService> services)
    {
        if (services is TService[] array)
            return array;

        if (services is ICollection<TService> collection)
        {
            if (collection.Count == 0)
                return [];

            if (collection.Count == 1)
            {
                if (collection is IList<TService> list)
                    return [list[0]];

                using var singleItemEnumerator = collection.GetEnumerator();
                _ = singleItemEnumerator.MoveNext();
                return [singleItemEnumerator.Current];
            }

            var copied = new TService[collection.Count];
            collection.CopyTo(copied, 0);
            return copied;
        }

        using var enumerator = services.GetEnumerator();
        if (!enumerator.MoveNext())
            return [];

        var first = enumerator.Current;
        if (!enumerator.MoveNext())
            return [first];

        var items = new List<TService>
        {
            first,
            enumerator.Current
        };

        while (enumerator.MoveNext())
            items.Add(enumerator.Current);

        return [.. items];
    }
}
