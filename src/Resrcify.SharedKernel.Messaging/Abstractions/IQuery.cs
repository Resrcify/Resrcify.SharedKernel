using MediatR;
using Resrcify.SharedKernel.ResultFramework.Primitives;

namespace Resrcify.SharedKernel.Messaging.Abstractions;

public interface IQuery<TResponse>
    : IRequest<Result<TResponse>>;
