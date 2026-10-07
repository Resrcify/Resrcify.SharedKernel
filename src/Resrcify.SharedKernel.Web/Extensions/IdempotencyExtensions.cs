using System;
using Microsoft.AspNetCore.Builder;
using Resrcify.SharedKernel.Web.Idempotency;

namespace Resrcify.SharedKernel.Web.Extensions;

/// <summary>Idempotency keys: a request repeating a key gets the first one's response instead of being handled again.</summary>
public static class IdempotencyExtensions
{
    /// <summary>
    /// Handles the idempotency keys of the endpoints marked <see cref="WithIdempotency{TBuilder}"/>. Place it after
    /// authentication (keys are per user). Needs an <c>ICachingService</c> and an <c>IClaimStore</c> registered (the
    /// Caching package's distributed cache is both).
    /// </summary>
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app, Action<IdempotencyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = new IdempotencyOptions();
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.HeaderName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Expiration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.InProgressTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxKeyLength, 1);
        return app.UseMiddleware<IdempotencyMiddleware>(options);
    }

    /// <summary>
    /// Makes the endpoint idempotent per key (<c>UseIdempotency</c>): a request repeating an <c>Idempotency-Key</c> gets
    /// the first one's response, 409 while the first is still handled, and 422 when the key was used for a different
    /// request. Without the header the request is handled as usual, unless <paramref name="requireKey"/> (then 400).
    /// </summary>
    public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder, bool requireKey = false)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new IdempotentEndpoint(requireKey));
    }
}
