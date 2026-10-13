using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Mediator.Abstractions;

internal interface IDiComposedSendRuntime<TRequest, TResponse> : ISendRuntimeExecutor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>;
