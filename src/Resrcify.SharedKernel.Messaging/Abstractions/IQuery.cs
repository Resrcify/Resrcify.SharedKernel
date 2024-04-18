using MediatR;
using Resrcify.SharedKernel.ResultFramework.Shared;

namespace Resrcify.SharedKernel.Messaging.Abstractions;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>
{
}
