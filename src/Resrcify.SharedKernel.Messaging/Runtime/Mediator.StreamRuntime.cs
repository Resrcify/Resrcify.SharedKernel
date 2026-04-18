using System.Collections.Generic;
using System.Threading;
using Resrcify.SharedKernel.Abstractions.Messaging;

namespace Resrcify.SharedKernel.Messaging.Runtime;

internal sealed partial class Mediator
{
    private sealed class StreamRuntime<TRequest, TResponse>(
        IStreamRequestHandler<TRequest, TResponse> handler,
        IStreamPipelineBehavior<TRequest, TResponse>[] behaviors)
        : IStreamRuntimeExecutor<TRequest, TResponse>
        where TRequest : IStreamRequest<TResponse>
    {
        private readonly StreamExecutor _executor = BuildExecutor(handler, behaviors);

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
    }
}
