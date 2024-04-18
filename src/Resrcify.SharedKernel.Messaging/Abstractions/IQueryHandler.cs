using MediatR;
using Resrcify.SharedKernel.ResultFramework.Shared;

namespace Resrcify.SharedKernel.Messaging.Abstractions;

public interface IQueryHandler<TQuery, TResponse>
    : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>
{
}
