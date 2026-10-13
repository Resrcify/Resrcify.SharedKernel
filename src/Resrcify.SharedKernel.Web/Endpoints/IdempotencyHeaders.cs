using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;

namespace Resrcify.SharedKernel.Web.Endpoints;

/// <summary>
/// The HTTP side of idempotent requests (<see cref="IIdempotentRequest"/>): the header a client sends its key in, and
/// the one a replayed answer carries.
/// </summary>
/// <example>
/// <code>
/// app.MapPostRequest("/shards",
///     (CreateShardBody body, [FromHeader(Name = IdempotencyHeaders.Key)] string? key) =&gt;
///         new CreateShardCommand(body.Name) { IdempotencyKey = key });
/// </code>
/// </example>
public static class IdempotencyHeaders
{
    /// <summary>The request header carrying the client's idempotency key.</summary>
    public const string Key = "Idempotency-Key";

    /// <summary>The response header (<c>true</c>) marking an answer given with an earlier result.</summary>
    public const string Replayed = "Idempotency-Replayed";

    /// <summary>
    /// Adds <see cref="Replayed"/> to the response when <paramref name="request"/> was answered with an earlier result.
    /// The request endpoints (<c>MapPostRequest</c>, …) do it; an endpoint of your own calls it after sending.
    /// </summary>
    public static void MarkIfReplayed(HttpContext context, object request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (context.RequestServices.GetService<IIdempotencyContext>()?.WasReplayed(request) == true)
            context.Response.Headers[Replayed] = "true";
    }
}
