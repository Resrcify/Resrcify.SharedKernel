using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.Messaging.Runtime;

internal sealed partial class Mediator
{
    private sealed class SendRuntime<TRequest, TResponse>(
        IRequestHandler<TRequest, TResponse> handler,
        IPipelineBehavior<TRequest, TResponse>[] behaviors,
        IRequestPipelineBehavior<TRequest, TResponse>[] requestBehaviors,
        IRequestPreProcessor<TRequest>[] preProcessors,
        IRequestPostProcessor<TRequest, TResponse>[] postProcessors)
        : ISendRuntimeExecutor<TRequest, TResponse>, ISendTaskRuntimeExecutor<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly RequestExecutionDelegate<TRequest, TResponse> _executor = BuildExecutor(handler, behaviors, requestBehaviors);

        public Task<TResponse> ExecuteTask(TRequest request, CancellationToken cancellationToken)
        {
            if (preProcessors.Length == 0 && postProcessors.Length == 0)
                return _executor(request, cancellationToken);

            return Execute(request, cancellationToken).AsTask();
        }

        public ValueTask<TResponse> Execute(TRequest request, CancellationToken cancellationToken)
        {
            if (preProcessors.Length == 0 && postProcessors.Length == 0)
            {
                return new ValueTask<TResponse>(_executor(request, cancellationToken));
            }

            for (var index = 0; index < preProcessors.Length; index++)
            {
                var preProcessTask = preProcessors[index].Process(request, cancellationToken);
                if (!preProcessTask.IsCompletedSuccessfully)
                    return new ValueTask<TResponse>(ExecuteSlow(request, index, preProcessTask, cancellationToken));
            }

            var responseTask = _executor(request, cancellationToken);
            if (!responseTask.IsCompletedSuccessfully)
                return new ValueTask<TResponse>(ExecuteSlow(request, preProcessors.Length, responseTask, cancellationToken));

            if (postProcessors.Length == 0)
                return new ValueTask<TResponse>(responseTask);

            var response = responseTask.GetAwaiter().GetResult();

            for (var index = 0; index < postProcessors.Length; index++)
            {
                var postProcessTask = postProcessors[index].Process(request, response, cancellationToken);
                if (!postProcessTask.IsCompletedSuccessfully)
                    return new ValueTask<TResponse>(ExecuteSlow(request, response, index, postProcessTask, cancellationToken));
            }

            return ValueTask.FromResult(response);
        }

        private async Task<TResponse> ExecuteSlow(
            TRequest request,
            int preProcessorIndex,
            Task pendingPreProcessorTask,
            CancellationToken cancellationToken)
        {
            await pendingPreProcessorTask.ConfigureAwait(false);

            for (var index = preProcessorIndex + 1; index < preProcessors.Length; index++)
                await preProcessors[index].Process(request, cancellationToken).ConfigureAwait(false);

            var response = await _executor(request, cancellationToken).ConfigureAwait(false);

            for (var index = 0; index < postProcessors.Length; index++)
                await postProcessors[index].Process(request, response, cancellationToken).ConfigureAwait(false);

            return response;
        }

        private async Task<TResponse> ExecuteSlow(
            TRequest request,
            int preProcessorCount,
            Task<TResponse> responseTask,
            CancellationToken cancellationToken)
        {
            for (var index = preProcessorCount; index < preProcessors.Length; index++)
                await preProcessors[index].Process(request, cancellationToken).ConfigureAwait(false);

            var response = await responseTask.ConfigureAwait(false);

            for (var index = 0; index < postProcessors.Length; index++)
                await postProcessors[index].Process(request, response, cancellationToken).ConfigureAwait(false);

            return response;
        }

        private async Task<TResponse> ExecuteSlow(
            TRequest request,
            TResponse response,
            int postProcessorIndex,
            Task pendingPostProcessorTask,
            CancellationToken cancellationToken)
        {
            await pendingPostProcessorTask.ConfigureAwait(false);

            for (var index = postProcessorIndex + 1; index < postProcessors.Length; index++)
                await postProcessors[index].Process(request, response, cancellationToken).ConfigureAwait(false);

            return response;
        }

        private static RequestExecutionDelegate<TRequest, TResponse> BuildExecutor(
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
    }

    private sealed class ValueTaskSendRuntime<TRequest, TResponse>(
        IValueTaskRequestHandler<TRequest, TResponse> handler,
        IValueTaskPipelineBehavior<TRequest, TResponse>[] behaviors,
        IValueTaskRequestPipelineBehavior<TRequest, TResponse>[] requestBehaviors,
        IRequestPreProcessor<TRequest>[] preProcessors,
        IRequestPostProcessor<TRequest, TResponse>[] postProcessors)
        : ISendRuntimeExecutor<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly ValueTaskRequestExecutionDelegate<TRequest, TResponse> _executor = BuildExecutor(handler, behaviors, requestBehaviors);

        public ValueTask<TResponse> Execute(TRequest request, CancellationToken cancellationToken)
        {
            if (preProcessors.Length == 0 && postProcessors.Length == 0)
                return _executor(request, cancellationToken);

            for (var index = 0; index < preProcessors.Length; index++)
            {
                var preProcessTask = preProcessors[index].Process(request, cancellationToken);
                if (!preProcessTask.IsCompletedSuccessfully)
                    return AwaitSlowPreProcessor(request, index, preProcessTask, cancellationToken);
            }

            var response = _executor(request, cancellationToken);
            if (!response.IsCompletedSuccessfully)
                return AwaitSlowResponse(request, preProcessors.Length, response, cancellationToken);

            if (postProcessors.Length == 0)
                return response;

            var result = response.Result;
            for (var index = 0; index < postProcessors.Length; index++)
            {
                var postProcessTask = postProcessors[index].Process(request, result, cancellationToken);
                if (!postProcessTask.IsCompletedSuccessfully)
                    return AwaitSlowPostProcessor(request, result, index, postProcessTask, cancellationToken);
            }

            return ValueTask.FromResult(result);
        }

        private async ValueTask<TResponse> AwaitSlowPreProcessor(
            TRequest request,
            int preProcessorIndex,
            Task pendingPreProcessorTask,
            CancellationToken cancellationToken)
        {
            await pendingPreProcessorTask.ConfigureAwait(false);

            for (var index = preProcessorIndex + 1; index < preProcessors.Length; index++)
                await preProcessors[index].Process(request, cancellationToken).ConfigureAwait(false);

            var result = await _executor(request, cancellationToken).ConfigureAwait(false);

            for (var index = 0; index < postProcessors.Length; index++)
                await postProcessors[index].Process(request, result, cancellationToken).ConfigureAwait(false);

            return result;
        }

        private async ValueTask<TResponse> AwaitSlowResponse(
            TRequest request,
            int preProcessorCount,
            ValueTask<TResponse> response,
            CancellationToken cancellationToken)
        {
            for (var index = preProcessorCount; index < preProcessors.Length; index++)
                await preProcessors[index].Process(request, cancellationToken).ConfigureAwait(false);

            var result = await response.ConfigureAwait(false);

            for (var index = 0; index < postProcessors.Length; index++)
                await postProcessors[index].Process(request, result, cancellationToken).ConfigureAwait(false);

            return result;
        }

        private async ValueTask<TResponse> AwaitSlowPostProcessor(
            TRequest request,
            TResponse response,
            int postProcessorIndex,
            Task pendingPostProcessorTask,
            CancellationToken cancellationToken)
        {
            await pendingPostProcessorTask.ConfigureAwait(false);

            for (var index = postProcessorIndex + 1; index < postProcessors.Length; index++)
                await postProcessors[index].Process(request, response, cancellationToken).ConfigureAwait(false);

            return response;
        }

        private static ValueTaskRequestExecutionDelegate<TRequest, TResponse> BuildExecutor(
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
    }
}
