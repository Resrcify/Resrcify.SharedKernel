using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Endpoints;

namespace Resrcify.SharedKernel.Web.Extensions;

/// <summary>
/// Minimal-API endpoints that send a mediator request and answer with its <see cref="Result"/>. The delegate is an
/// ordinary Minimal-API handler (route values, query, headers, body, <c>[AsParameters]</c>, services,
/// <c>HttpContext</c>, a <c>CancellationToken</c>, sync or async) that returns the request; the endpoint sends it
/// through <see cref="ISender"/> and answers:
/// <list type="bullet">
/// <item>200 with the value for a request answered with <c>Result&lt;T&gt;</c> (a query, a command with a result);</item>
/// <item>204 for one answered with <c>Result</c> (a command without a result);</item>
/// <item><paramref name="onSuccess"/>'s answer when given, e.g. <c>TypedResults.Created(...)</c> or <c>TypedResults.Accepted(...)</c>;</item>
/// <item>problem details for a failure (<see cref="RequestEndpointOptions.OnFailure"/>, or the endpoint's
/// <paramref name="onFailure"/>).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Any request answered with a <see cref="Result"/> works: <c>ICommand</c>, <c>ICommand&lt;T&gt;</c>, <c>IQuery&lt;T&gt;</c>,
/// <c>ICachingQuery&lt;T&gt;</c>, <c>ITransactionCommand</c>, or a type of your own implementing
/// <c>IRequest&lt;Result&gt;</c> / <c>IRequest&lt;Result&lt;T&gt;&gt;</c>. It goes through the mediator's whole pipeline.
/// A delegate returning anything else throws when the endpoint is mapped, not when it is called.
/// </para>
/// <para>
/// OpenAPI: the success response (the value's type, 204, or what <paramref name="onSuccess"/>'s typed result declares)
/// and problem details for <see cref="RequestEndpointOptions.ProblemStatusCodes"/>. The request itself is never
/// described as a response. A filter added to the endpoint runs inside this one and may answer instead (validation).
/// </para>
/// <para>
/// An <see cref="IIdempotentRequest"/> answered with an earlier result (sent again with its key) is marked
/// <c>Idempotency-Replayed: true</c> (<see cref="IdempotencyHeaders"/>).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// app.MapPostRequest("/shards/{shardId:guid}/members/{allyCode:long}",
///     (Guid shardId, long allyCode, AddMemberBody body) =&gt; new AddShardMemberCommand(shardId, allyCode, body.Emoji));
/// app.MapGetRequest("/shards/{shardId:guid}", (Guid shardId) =&gt; new GetShardQuery(shardId));
/// app.MapPostRequest("/shards", (CreateShardBody body) =&gt; new CreateShardCommand(body.Name),
///     onSuccess: (ShardDto shard) =&gt; TypedResults.Created($"/shards/{shard.Id}", shard));
/// </code>
/// </example>
public static class RequestEndpointExtensions
{
    /// <summary>A GET endpoint for the request <paramref name="toRequest"/> returns.</summary>
    /// <param name="toRequest">A Minimal-API handler returning the request (or a task of it).</param>
    /// <param name="onSuccess">
    /// The answer to success instead of 200/204: a delegate taking the result's value (or nothing) and returning an
    /// <see cref="IResult"/>; a <c>TypedResults</c> type declares itself for OpenAPI.
    /// </param>
    /// <param name="onFailure">The answer to a failed result instead of <see cref="RequestEndpointOptions.OnFailure"/>.</param>
    public static RouteHandlerBuilder MapGetRequest(
        this IEndpointRouteBuilder app,
        [StringSyntax("Route")] string pattern,
        Delegate toRequest,
        Delegate? onSuccess = null,
        Func<Result, IResult>? onFailure = null)
        => app.MapRequest(pattern, [HttpMethods.Get], toRequest, onSuccess, onFailure);

    /// <inheritdoc cref="MapGetRequest"/>
    /// <summary>A POST endpoint for the request <paramref name="toRequest"/> returns.</summary>
    public static RouteHandlerBuilder MapPostRequest(
        this IEndpointRouteBuilder app,
        [StringSyntax("Route")] string pattern,
        Delegate toRequest,
        Delegate? onSuccess = null,
        Func<Result, IResult>? onFailure = null)
        => app.MapRequest(pattern, [HttpMethods.Post], toRequest, onSuccess, onFailure);

    /// <inheritdoc cref="MapGetRequest"/>
    /// <summary>A PUT endpoint for the request <paramref name="toRequest"/> returns.</summary>
    public static RouteHandlerBuilder MapPutRequest(
        this IEndpointRouteBuilder app,
        [StringSyntax("Route")] string pattern,
        Delegate toRequest,
        Delegate? onSuccess = null,
        Func<Result, IResult>? onFailure = null)
        => app.MapRequest(pattern, [HttpMethods.Put], toRequest, onSuccess, onFailure);

    /// <inheritdoc cref="MapGetRequest"/>
    /// <summary>A PATCH endpoint for the request <paramref name="toRequest"/> returns.</summary>
    public static RouteHandlerBuilder MapPatchRequest(
        this IEndpointRouteBuilder app,
        [StringSyntax("Route")] string pattern,
        Delegate toRequest,
        Delegate? onSuccess = null,
        Func<Result, IResult>? onFailure = null)
        => app.MapRequest(pattern, [HttpMethods.Patch], toRequest, onSuccess, onFailure);

    /// <inheritdoc cref="MapGetRequest"/>
    /// <summary>A DELETE endpoint for the request <paramref name="toRequest"/> returns.</summary>
    public static RouteHandlerBuilder MapDeleteRequest(
        this IEndpointRouteBuilder app,
        [StringSyntax("Route")] string pattern,
        Delegate toRequest,
        Delegate? onSuccess = null,
        Func<Result, IResult>? onFailure = null)
        => app.MapRequest(pattern, [HttpMethods.Delete], toRequest, onSuccess, onFailure);

    /// <inheritdoc cref="MapGetRequest"/>
    /// <summary>An endpoint for <paramref name="httpMethods"/> and the request <paramref name="toRequest"/> returns.</summary>
    public static RouteHandlerBuilder MapRequest(
        this IEndpointRouteBuilder app,
        [StringSyntax("Route")] string pattern,
        IEnumerable<string> httpMethods,
        Delegate toRequest,
        Delegate? onSuccess = null,
        Func<Result, IResult>? onFailure = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(toRequest);
        var options = app.ServiceProvider.GetService<IOptions<RequestEndpointOptions>>()?.Value ?? new RequestEndpointOptions();
        var requestType = RequestTypeOf(toRequest.Method.ReturnType);
        var responder = RequestResponder.For(requestType, onSuccess, onFailure ?? options.OnFailure);

        var builder = app
            .MapMethods(pattern, httpMethods, toRequest)
            .AddEndpointFilter(async (context, next) =>
            {
                var request = await next(context);
                // A filter inside this one answered instead (validation, say).
                if (request is IResult answered)
                    return answered;
                var http = context.HttpContext;
                if (request is null)
                    throw new InvalidOperationException($"The endpoint {http.Request.Method} {pattern} returned no request to send.");
                var answer = await responder.RespondAsync(http.RequestServices.GetRequiredService<ISender>(), request, http.RequestAborted);
                IdempotencyHeaders.MarkIfReplayed(http, request);
                return answer;
            });

        // ASP.NET describes the delegate's return value as the 200 response: it is the request, never sent back.
        builder.Finally(endpoint =>
        {
            for (var index = endpoint.Metadata.Count - 1; index >= 0; index--)
                if (endpoint.Metadata[index] is IProducesResponseTypeMetadata produces && produces.Type == requestType)
                    endpoint.Metadata.RemoveAt(index);
        });

        if (onSuccess is not null)
            builder.Add(endpoint => RequestResponder.DescribeSuccess(onSuccess, endpoint));
        else if (responder.ResponseType is { } responseType)
            builder.Produces(StatusCodes.Status200OK, responseType);
        else
            builder.Produces(StatusCodes.Status204NoContent);
        foreach (var statusCode in options.ProblemStatusCodes)
            builder.ProducesProblem(statusCode);
        return builder;
    }

    private static Type RequestTypeOf(Type returnType)
    {
        if (!returnType.IsGenericType)
            return returnType;
        var definition = returnType.GetGenericTypeDefinition();
        return definition == typeof(Task<>) || definition == typeof(ValueTask<>)
            ? returnType.GetGenericArguments()[0]
            : returnType;
    }
}
