using System;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Caching;

namespace Resrcify.SharedKernel.Web.Idempotency;

/// <summary>Marks an endpoint as idempotent per key (<c>WithIdempotency()</c>).</summary>
internal sealed record IdempotentEndpoint(bool RequireKey);

/// <summary>A response kept for its key, and the request it answered.</summary>
internal sealed record IdempotentResponse(string Fingerprint, int StatusCode, string? ContentType, string? Location, byte[] Body);

/// <summary>
/// Answers a request repeating an idempotency key with the response the first one got, instead of handling it again.
/// The key is held while the first request is handled (a repeat meanwhile gets 409), through <see cref="IClaimStore"/>,
/// and its response is kept in <see cref="ICachingService"/> for <see cref="IdempotencyOptions.Expiration"/>. A
/// response of 500 or more isn't kept, so the client can try again; neither is one whose handling threw.
/// </summary>
/// <remarks>
/// Keys are per user (the name identifier or <c>sub</c> claim; anonymous callers share one scope). A key repeated with a
/// different request (method, path, query or body) is answered 422.
/// </remarks>
internal sealed class IdempotencyMiddleware(RequestDelegate next, IdempotencyOptions options)
{
    /// <summary>The response header that marks a replayed response.</summary>
    public const string ReplayedHeader = "Idempotency-Replayed";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotentEndpoint>() is not { } endpoint)
        {
            await next(context);
            return;
        }

        string? key = context.Request.Headers[options.HeaderName];
        if (string.IsNullOrWhiteSpace(key))
        {
            if (endpoint.RequireKey)
                await ProblemAsync(context, StatusCodes.Status400BadRequest, $"The {options.HeaderName} header is required.");
            else
                await next(context);
            return;
        }

        if (key.Length > options.MaxKeyLength)
        {
            await ProblemAsync(context, StatusCodes.Status400BadRequest, $"The {options.HeaderName} header is longer than {options.MaxKeyLength} characters.");
            return;
        }

        await HandleAsync(context, key);
    }

    private async Task HandleAsync(HttpContext context, string key)
    {
        var cache = context.RequestServices.GetRequiredService<ICachingService>();
        var claims = context.RequestServices.GetRequiredService<IClaimStore>();
        var cancellationToken = context.RequestAborted;
        var cacheKey = $"idempotency:{UserOf(context.User)}:{key}";
        var fingerprint = await FingerprintAsync(context.Request, cancellationToken);

        if (await cache.GetAsync<IdempotentResponse>(cacheKey, cancellationToken) is { } kept)
        {
            await ReplayAsync(context, kept, fingerprint);
            return;
        }

        var lockKey = $"{cacheKey}:in-progress";
        if (!await claims.TryClaimForAsync(lockKey, options.InProgressTimeout, cancellationToken))
        {
            // Another request with the key is being handled, or has just finished.
            if (await cache.GetAsync<IdempotentResponse>(cacheKey, cancellationToken) is { } finished)
                await ReplayAsync(context, finished, fingerprint);
            else
                await InProgressAsync(context);
            return;
        }

        try
        {
            // It may have finished between the first look and the claim.
            if (await cache.GetAsync<IdempotentResponse>(cacheKey, cancellationToken) is { } finished)
            {
                await ReplayAsync(context, finished, fingerprint);
                return;
            }

            var response = await HandleAndCaptureAsync(context, fingerprint);
            if (response.StatusCode < StatusCodes.Status500InternalServerError)
                await cache.SetForAsync(cacheKey, response, options.Expiration, CancellationToken.None);
        }
        finally
        {
            await claims.ReleaseAsync(lockKey, CancellationToken.None);
        }
    }

    private async Task<IdempotentResponse> HandleAndCaptureAsync(HttpContext context, string fingerprint)
    {
        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = original;
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(original, context.RequestAborted);
        return new IdempotentResponse(
            fingerprint,
            context.Response.StatusCode,
            context.Response.ContentType,
            context.Response.Headers.Location,
            buffer.ToArray());
    }

    private static async Task ReplayAsync(HttpContext context, IdempotentResponse kept, string fingerprint)
    {
        if (!string.Equals(kept.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            await ProblemAsync(context, StatusCodes.Status422UnprocessableEntity, "The idempotency key was already used for a different request.");
            return;
        }

        context.Response.StatusCode = kept.StatusCode;
        if (kept.ContentType is not null)
            context.Response.ContentType = kept.ContentType;
        if (kept.Location is not null)
            context.Response.Headers.Location = kept.Location;
        context.Response.Headers[ReplayedHeader] = "true";
        await context.Response.Body.WriteAsync(kept.Body, context.RequestAborted);
    }

    private static async Task InProgressAsync(HttpContext context)
    {
        context.Response.Headers.RetryAfter = "1";
        await ProblemAsync(context, StatusCodes.Status409Conflict, "A request with this idempotency key is still being handled.");
    }

    private static Task ProblemAsync(HttpContext context, int statusCode, string detail)
        => Microsoft.AspNetCore.Http.Results.Problem(detail: detail, statusCode: statusCode).ExecuteAsync(context);

    private static string UserOf(ClaimsPrincipal user)
        => user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value ?? "anonymous";

    // The request a key was used for: method, path, query and body.
    private static async Task<string> FingerprintAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{request.Method} {request.Path}{request.QueryString}\n"));
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
            hash.AppendData(chunk, 0, read);
        request.Body.Position = 0;
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
