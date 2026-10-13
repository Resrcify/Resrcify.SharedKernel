using System.Collections.Generic;
using System.Threading;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Mediator.Abstractions;

internal interface IStreamRuntimeExecutor<TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Create(TRequest request, CancellationToken cancellationToken);
}
