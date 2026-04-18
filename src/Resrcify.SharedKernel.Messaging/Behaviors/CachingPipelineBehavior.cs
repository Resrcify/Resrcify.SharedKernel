using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Messaging.Behaviors;

public class CachingPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICachingQuery
    where TResponse : Result
{
    private readonly ICachingService _cachingService;
    private readonly ILogger _logger;
    public CachingPipelineBehavior(
        ICachingService cachingService,
        ILogger<CachingPipelineBehavior<TRequest, TResponse>> logger)
    {
        _cachingService = cachingService;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.CacheKey))
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "{RequestName}: Key property not set",
                    typeof(TRequest).Name);
            }

            return await next(cancellationToken);
        }

        TResponse? cacheResult = await _cachingService.GetAsync<TResponse>(
            request.CacheKey,
            cancellationToken: cancellationToken);

        if (cacheResult is not null)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "{RequestName}: Cache hit",
                    typeof(TRequest).Name);
            }

            return cacheResult;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "{RequestName}: Cache miss",
                typeof(TRequest).Name);
        }

        var result = await next(cancellationToken);

        if (result.IsSuccess)
        {
            await _cachingService.SetAsync(
                request.CacheKey,
                result,
                request.Expiration,
                cancellationToken: cancellationToken);
        }

        return result;
    }
}
