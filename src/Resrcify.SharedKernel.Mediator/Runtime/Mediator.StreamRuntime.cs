using System.Collections.Generic;
using System.Threading;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Abstractions;

namespace Resrcify.SharedKernel.Mediator.Runtime;

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
            => new StreamPipeline<TRequest, TResponse>(streamHandler, pipelineBehaviors).Invoke;
    }
}
