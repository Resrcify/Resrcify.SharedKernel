using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>
/// Runs an <see cref="ITransactionalCommand"/> in a transaction (<c>IUnitOfWork.ExecuteInTransactionAsync</c>): committed
/// on a successful result, rolled back on a failure or an exception. An exception is logged at
/// <see cref="LogLevel.Error"/>, or at <see cref="LogLevel.Warning"/> when it is transient (by the container's
/// <see cref="ITransientFailureClassifier"/>, else <see cref="TransientFailures"/>).
/// </summary>
public class TransactionPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, ITransactionalCommand
    where TResponse : Result
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger _logger;
    private readonly ITransientFailureClassifier? _transientFailures;

    public TransactionPipelineBehavior(
        IUnitOfWork unitOfWork,
        ILogger<TransactionPipelineBehavior<TRequest, TResponse>> logger,
        ITransientFailureClassifier? transientFailures = null)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _transientFailures = transientFailures;
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
            // transaction. Without a CommandTimeout of its own the command keeps the
            // DbContext's (e.g. Database:CommandTimeoutInSeconds).
            return await _unitOfWork.ExecuteInTransactionAsync(
                token => next(token),
                request.IsolationLevel ?? System.Data.IsolationLevel.ReadCommitted,
                request.CommandTimeout,
                cancellationToken);
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

        var level = TransientFailureLevels.Of(exception, _transientFailures);
        if (_logger.IsEnabled(level) && LoggedExceptions.Claim(exception))
        {
            _logger.Log(
                level,
                exception,
                "Request {@RequestName} threw {@ExceptionType}; its transaction is rolled back",
                typeof(TRequest).Name,
                exception.GetType().Name);
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Request {@RequestName}: its transaction is rolled back after {@ExceptionType}",
                typeof(TRequest).Name,
                exception.GetType().Name);
        }

        return false;
    }
}
