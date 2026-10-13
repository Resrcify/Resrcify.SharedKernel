using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Mediator.Abstractions;

internal interface ISendTaskRuntimeExecutor<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> ExecuteTask(TRequest request, CancellationToken cancellationToken);
}
