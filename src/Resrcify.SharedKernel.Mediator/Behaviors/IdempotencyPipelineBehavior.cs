using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Abstractions.Caching;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>
/// Answers an <see cref="IIdempotentRequest"/> sent again with its key with the first one's result, instead of handling
/// it again, however it arrives (an HTTP endpoint, a message consumer, a job).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>No key: handled as usual.</item>
/// <item>The first request with a key holds the key while it is handled (<see cref="IClaimStore"/>); a repeat meanwhile is
/// answered <see cref="IdempotencyErrors.InProgress"/> (a Conflict, 409).</item>
/// <item>Its result is kept for <see cref="IdempotencyPipelineOptions.Expiration"/> (<see cref="ICachingService"/>) and
/// given to a repeat, which is marked replayed (<see cref="IIdempotencyContext"/>).</item>
/// <item>A key repeated with a different request (its JSON, the key and scope left out) is answered
/// <see cref="IdempotencyErrors.KeyReused"/> (Unprocessable, 422).</item>
/// <item>A failure another try may pass (a transient one) isn't kept, nor is a handler that threw: a repeat runs again.</item>
/// </list>
/// It runs before validation (a standard behavior, right after logging), so a repeat gets the first answer though a
/// validation rule that reads the data would refuse it now ("the shard must not exist yet"), and a repeat of a request
/// refused by validation gets the same refusal. And it runs outside the transaction and the unit of work, so it keeps a
/// result only once it is committed, and a repeat opens no transaction. Keys are kept per request type and
/// <see cref="IIdempotentRequest.IdempotencyScope"/>.
/// </remarks>
public sealed partial class IdempotencyPipelineBehavior<TRequest, TResponse>(
    IServiceProvider services,
    ILogger<IdempotencyPipelineBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IIdempotentRequest
    where TResponse : Result
{
    private readonly IdempotencyPipelineOptions _options = services.GetService<IdempotencyPipelineOptions>() ?? new();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(next);
        var key = request.IdempotencyKey;
        if (string.IsNullOrEmpty(key))
            return await next(cancellationToken);
        if (string.IsNullOrWhiteSpace(key) || key.Length > _options.MaxKeyLength)
            return ResultFactory.Failure<TResponse>(IdempotencyErrors.InvalidKey(_options.MaxKeyLength));

        var cache = services.GetService<ICachingService>();
        // The registered claim store, or the cache when it is one too (as DistributedCachingService is), so a service
        // that registered its cache needs nothing more.
        var claims = services.GetService<IClaimStore>() ?? cache as IClaimStore;
        if (cache is null || claims is null)
            throw new InvalidOperationException(
                $"{typeof(TRequest).Name} was sent with an idempotency key, which needs an ICachingService that is also " +
                "an IClaimStore (the Caching package's DistributedCachingService over an IDistributedCache is both), or " +
                "an IClaimStore registered next to it.");

        var storeKey = $"idempotency:{typeof(TRequest).FullName}:{request.IdempotencyScope}:{key}";
        var fingerprint = IdempotencyFingerprint.Of(request);
        if (await cache.GetAsync<KeptResult>(storeKey, cancellationToken) is { } kept)
            return Replay(request, kept, fingerprint);

        var holdKey = $"{storeKey}:in-progress";
        if (!await claims.TryClaimForAsync(holdKey, _options.InProgressTimeout, cancellationToken))
            // Another request with the key is being handled, or has just finished.
            return await cache.GetAsync<KeptResult>(storeKey, cancellationToken) is { } finished
                ? Replay(request, finished, fingerprint)
                : ResultFactory.Failure<TResponse>(IdempotencyErrors.InProgress);

        try
        {
            // It may have finished between the first look and the claim.
            if (await cache.GetAsync<KeptResult>(storeKey, cancellationToken) is { } finished)
                return Replay(request, finished, fingerprint);

            var result = await next(cancellationToken);
            if (result.IsSuccess || result.Errors.All(error => !error.IsTransient()))
                await cache.SetForAsync(storeKey, new KeptResult(fingerprint, result), _options.Expiration, CancellationToken.None);
            return result;
        }
        finally
        {
            await claims.ReleaseAsync(holdKey, CancellationToken.None);
        }
    }

    private TResponse Replay(TRequest request, KeptResult kept, string fingerprint)
    {
        if (!string.Equals(kept.Fingerprint, fingerprint, StringComparison.Ordinal))
            return ResultFactory.Failure<TResponse>(IdempotencyErrors.KeyReused);

        services.GetService<IdempotencyContext>()?.MarkReplayed(request);
        LogReplayed(typeof(TRequest).Name);
        return kept.Result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{RequestName}: answered with the result kept for its idempotency key")]
    private partial void LogReplayed(string requestName);

    /// <summary>A result kept for a key, and the request it answered.</summary>
    internal sealed record KeptResult(string Fingerprint, TResponse Result);
}
