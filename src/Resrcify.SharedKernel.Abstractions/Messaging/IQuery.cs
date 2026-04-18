using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.Messaging;

public interface IQuery<TResponse>
    : IRequest<Result<TResponse>>;

