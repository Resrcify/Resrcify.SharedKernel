using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.ResultFramework.Primitives;

namespace Resrcify.SharedKernel.Messaging.Behaviors;

public class LoggingPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    private readonly ILogger<LoggingPipelineBehavior<TRequest, TResponse>> _logger;
    public LoggingPipelineBehavior(ILogger<LoggingPipelineBehavior<TRequest, TResponse>> logger)
        => _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        _logger.LogInformation("Starting request {@RequestName}, {@DateTimeUtc}",
            typeof(TRequest).Name,
            start);

        var result = await next(cancellationToken);

        var end = DateTime.UtcNow;
        var differenceMs = (end - start).TotalMilliseconds;
        if (result.IsFailure)
            _logger.LogInformation("Request failure {@RequestName}, {@Error}, {@DateTimeUtc} ({@DifferenceMs} ms)",
                typeof(TRequest).Name,
                result.Errors,
                end,
                differenceMs);

        _logger.LogInformation("Completed request {@RequestName}, {@DateTimeUtc} ({@DifferenceMs} ms)",
            typeof(TRequest).Name,
            end,
            differenceMs);
        return result;
    }
}