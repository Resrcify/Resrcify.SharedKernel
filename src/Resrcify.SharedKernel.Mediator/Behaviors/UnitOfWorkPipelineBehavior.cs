using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>
/// Saves a command's work (<c>IUnitOfWork.CompleteAsync</c>) when its handler succeeded. With
/// <see cref="UnitOfWorkPipelineOptions.ReturnPersistenceFailures"/>, a save the database refuses in a way the caller can
/// answer (a concurrency conflict, a duplicate) is returned as the command's failure instead of thrown.
/// </summary>
public class UnitOfWorkPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IBaseCommand
    where TResponse : Result
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger _logger;
    private readonly UnitOfWorkPipelineOptions _options;

    public UnitOfWorkPipelineBehavior(
        IUnitOfWork unitOfWork,
        ILogger<UnitOfWorkPipelineBehavior<TRequest, TResponse>> logger,
        UnitOfWorkPipelineOptions? options = null)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _options = options ?? new UnitOfWorkPipelineOptions();
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await next(cancellationToken);
            if (response is not { IsSuccess: true })
                return response;

            if (!_options.ReturnPersistenceFailures)
            {
                await _unitOfWork.CompleteAsync(cancellationToken);
                return response;
            }

            var saved = await _unitOfWork.TryCompleteAsync(cancellationToken);
            return saved.IsSuccess
                ? response
                : ResultFactory.Failure<TResponse>(saved.Errors);
        }
        catch (Exception ex) when (LogUnlessCancelled(ex, cancellationToken))
        {
            throw;   // never reached: the filter logs and lets the exception go on unchanged
        }
    }

    // Logs from the exception filter, which doesn't catch: the caller gets the original exception
    // (a concurrency conflict, a timeout) and can handle it by type. A cancellation the caller asked
    // for isn't an error, so it isn't logged. The exception is logged at Error once in the pipeline,
    // by the first behavior it leaves, or by whoever saw it first (LoggedExceptions); after that, a
    // Debug line without the stack.
    private bool LogUnlessCancelled(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        if (_logger.IsEnabled(LogLevel.Error) && LoggedExceptions.Claim(exception))
        {
            _logger.LogError(
                exception,
                "Request {@RequestName} threw {@ExceptionType}; nothing it changed is saved",
                typeof(TRequest).Name,
                exception.GetType().Name);
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Request {@RequestName}: nothing it changed is saved after {@ExceptionType}",
                typeof(TRequest).Name,
                exception.GetType().Name);
        }

        return false;
    }
}
