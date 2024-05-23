using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Caching.Abstractions;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Shared;

namespace Resrcify.SharedKernel.Messaging.Behaviors;

public class CachingPipelineBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICacheable
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

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        string requestName = typeof(TRequest).Name;

        if (string.IsNullOrEmpty(request.CacheKey))
        {
            _logger.LogInformation("{RequestName}: Key property not set", requestName);
            return await next();
        }

        TResponse? cacheResult = await _cachingService.GetAsync<TResponse>(
            request.CacheKey,
            cancellationToken);

        if (cacheResult is not null)
        {
            _logger.LogInformation("{RequestName}: Cache hit", requestName);
            return cacheResult;
        }

        _logger.LogInformation("{RequestName}: Cache miss", requestName);
        var result = await next();

        if (result.IsSuccess)
        {
            await _cachingService.SetAsync(
                request.CacheKey,
                result,
                request.Expiration,
                cancellationToken);
        }

        return result;
    }
}