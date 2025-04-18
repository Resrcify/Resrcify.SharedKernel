using System.Threading;
using System.Threading.Tasks;
using MediatR;
using System;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Primitives;

namespace Resrcify.SharedKernel.Messaging.Behaviors;

public class UnitOfWorkPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IBaseCommand
    where TResponse : Result
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger _logger;
    public UnitOfWorkPipelineBehavior(
        IUnitOfWork unitOfWork,
        ILogger<UnitOfWorkPipelineBehavior<TRequest, TResponse>> logger)
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
            var response = await next(cancellationToken);
            if (response is Result { IsSuccess: true })
                await _unitOfWork.CompleteAsync(cancellationToken);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception caught in UnitOfWorkPipelineBehavior");
            throw new InvalidOperationException("An error occurred while processing the UnitOfWorkPipelineBehavior.", ex);
        }
    }
}