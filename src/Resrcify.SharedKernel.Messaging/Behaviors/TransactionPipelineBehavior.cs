using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Shared;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;

namespace Resrcify.SharedKernel.Messaging.Behaviors;

public class TransactionPipelineBehavior<TRequest, TResponse>
: IPipelineBehavior<TRequest, TResponse>
    where TRequest : ITransactionalCommand
    where TResponse : Result
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger _logger;
    public TransactionPipelineBehavior(IUnitOfWork unitOfWork, ILogger<TransactionPipelineBehavior<TRequest, TResponse>> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            var commandTimeout = request.CommandTimeout ?? TimeSpan.FromSeconds(30);
            var isolationLevel = request.IsolationLevel ?? System.Data.IsolationLevel.ReadCommitted;
            await _unitOfWork.BeginTransactionAsync(isolationLevel, commandTimeout, cancellationToken);
            var response = await next();
            if (response is Result { IsSuccess: true })
            {
                await _unitOfWork.CompleteAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                return response;
            }
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError("Exception caught in UnitOfWorkPipelineBehavior: @{exception}", ex);
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}