using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Mediator.Abstractions;

internal interface IDiComposedStreamRuntime<TRequest, TResponse> : IStreamRuntimeExecutor<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>;
