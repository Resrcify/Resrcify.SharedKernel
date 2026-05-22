using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Messaging.Behaviors;

public class TransactionPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, ITransactionalCommand
    where TResponse : Result
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger _logger;

    public TransactionPipelineBehavior(
        IUnitOfWork unitOfWork,
        ILogger<TransactionPipelineBehavior<TRequest, TResponse>> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            // ExecuteInTransactionAsync commits only on a successful Result, rolls
            // back on a failure Result or an exception, and always disposes the
            // transaction.
            return await _unitOfWork.ExecuteInTransactionAsync(
                token => next(token),
                request.IsolationLevel ?? System.Data.IsolationLevel.ReadCommitted,
                request.CommandTimeout ?? TimeSpan.FromSeconds(30),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception caught in TransactionPipelineBehavior");
            throw new InvalidOperationException("An error occurred while processing the TransactionPipelineBehavior.", ex);
        }
    }
}
