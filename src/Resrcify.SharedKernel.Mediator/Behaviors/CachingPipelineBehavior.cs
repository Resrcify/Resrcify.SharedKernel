using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

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

        // Nothing to keep a result for: it would expire at once (and a cache refuses an expiry in the past).
        if (request.Expiration <= TimeSpan.Zero)
            return await next(cancellationToken);

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
            // Kept for Expiration from now: a result that is read often still expires on time (a sliding
            // expiry would be kept alive by its readers). A longer Expiration than a cache keeps anything (3.x's
            // TimeSpan.MaxValue, "keep it") is kept for that long: the most there is.
            await _cachingService.SetForAsync(
                request.CacheKey,
                result,
                request.Expiration > ICachingService.MaxLifetime ? ICachingService.MaxLifetime : request.Expiration,
                cancellationToken);
        }

        return result;
    }
}
