using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.Mediator;

public interface IQuery<TResponse>
    : IRequest<Result<TResponse>>;

