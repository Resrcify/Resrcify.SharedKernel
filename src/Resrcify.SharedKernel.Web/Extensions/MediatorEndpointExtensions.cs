using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Web.Extensions;

/// <summary>
/// Minimal-API endpoints that send a command or query through the mediator: the request is bound
/// (<c>[AsParameters]</c>: route values, query string, headers and body as properties of <typeparamref name="TRequest"/>),
/// turned into the command or query, sent, and its <see cref="Result"/> written as the response (200 with the value,
/// 204 for a command without one) or as problem details (<see cref="HttpResultExtensions.ToProblemDetails"/>).
/// </summary>
/// <remarks>
/// Each endpoint declares its responses for OpenAPI: the success response, and problem details for 400, 404, 409 and
/// 500 (add others with <c>ProducesProblem</c>). They return the <see cref="RouteHandlerBuilder"/>, so the rest is
/// chained as usual: <c>WithName</c>, <c>WithTags</c>, <c>RequireAuthorization</c>, the API version.
/// </remarks>
/// <example>
/// <code>
/// internal sealed record AddShardMemberRequest(
///     [FromRoute] Guid ShardId,
///     [FromRoute] long AllyCode,
///     [FromBody] AddShardMemberCommandRequest Body);
///
/// app.MapPostCommand&lt;AddShardMemberRequest, AddShardMemberCommand&gt;(
///         ApiEndpoints.Shards.AddShardMember,
///         request =&gt; new(request.ShardId, request.AllyCode, request.Body.Alignment, request.Body.Emoji))
///     .WithName("Shards.AddShardMember")
///     .WithTags(ApiEndpoints.Shards.Tag);
/// </code>
/// </example>
public static class MediatorEndpointExtensions
{
    /// <summary>A GET endpoint for a query that takes nothing from the request: 200 with its result.</summary>
    public static RouteHandlerBuilder MapGetQuery<TQuery, TResponse>(
        this IEndpointRouteBuilder app,
        string pattern,
        Func<TQuery> query)
        where TQuery : IQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(query);
        return app
            .MapGet(
                pattern,
                async ([FromServices] ISender sender, CancellationToken cancellationToken) =>
                    await Result
                        .Create(query())
                        .Bind(request => sender.Send(request, cancellationToken))
                        .Match(Microsoft.AspNetCore.Http.Results.Ok, HttpResultExtensions.ToProblemDetails))
            .Produces<TResponse>()
            .ProducesStandardProblems();
    }

    /// <summary>A GET endpoint for a query: 200 with its result.</summary>
    public static RouteHandlerBuilder MapGetQuery<TRequest, TQuery, TResponse>(
        this IEndpointRouteBuilder app,
        string pattern,
        Func<TRequest, TQuery> toQuery)
        where TQuery : IQuery<TResponse>
        => app.MapGetQuery<TRequest, TQuery, TResponse, TResponse>(pattern, toQuery, response => response);

    /// <summary>A GET endpoint for a query: 200 with the part of its result <paramref name="toBody"/> picks.</summary>
    public static RouteHandlerBuilder MapGetQuery<TRequest, TQuery, TResponse, TBody>(
        this IEndpointRouteBuilder app,
        string pattern,
        Func<TRequest, TQuery> toQuery,
        Func<TResponse, TBody> toBody)
        where TQuery : IQuery<TResponse>
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(toQuery);
        ArgumentNullException.ThrowIfNull(toBody);
        return app
            .MapGet(
                pattern,
                async ([AsParameters] TRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
                    await Result
                        .Create(toQuery(request))
                        .Bind(query => sender.Send(query, cancellationToken))
                        .Match(response => Microsoft.AspNetCore.Http.Results.Ok(toBody(response)), HttpResultExtensions.ToProblemDetails))
            .Produces<TBody>()
            .ProducesStandardProblems();
    }

    /// <summary>A POST endpoint for a command without a result: 204.</summary>
    public static RouteHandlerBuilder MapPostCommand<TRequest, TCommand>(
        this IEndpointRouteBuilder app,
        string pattern,
        Func<TRequest, TCommand> toCommand)
        where TCommand : ICommand
        => app.MapCommand<TRequest, TCommand>(HttpMethods.Post, pattern, toCommand);

    /// <summary>A POST endpoint for a command with a result: 200 with it.</summary>
    public static RouteHandlerBuilder MapPostCommand<TRequest, TCommand, TResponse>(
        this IEndpointRouteBuilder app,
        string pattern,
        Func<TRequest, TCommand> toCommand)
        where TCommand : ICommand<TResponse>
        => app.MapCommand<TRequest, TCommand, TResponse>(HttpMethods.Post, pattern, toCommand);

    /// <summary>A PUT endpoint for a command without a result: 204.</summary>
    public static RouteHandlerBuilder MapPutCommand<TRequest, TCommand>(
        this IEndpointRouteBuilder app,
        string pattern,
        Func<TRequest, TCommand> toCommand)
        where TCommand : ICommand
        => app.MapCommand<TRequest, TCommand>(HttpMethods.Put, pattern, toCommand);

    /// <summary>A PUT endpoint for a command with a result: 200 with it.</summary>
    public static RouteHandlerBuilder MapPutCommand<TRequest, TCommand, TResponse>(
        this IEndpointRouteBuilder app,
        string pattern,
        Func<TRequest, TCommand> toCommand)
        where TCommand : ICommand<TResponse>
        => app.MapCommand<TRequest, TCommand, TResponse>(HttpMethods.Put, pattern, toCommand);

    /// <summary>A PATCH endpoint for a command without a result: 204.</summary>
    public static RouteHandlerBuilder MapPatchCommand<TRequest, TCommand>(
        this IEndpointRouteBuilder app,
        string pattern,
        Func<TRequest, TCommand> toCommand)
        where TCommand : ICommand
        => app.MapCommand<TRequest, TCommand>(HttpMethods.Patch, pattern, toCommand);

    /// <summary>A PATCH endpoint for a command with a result: 200 with it.</summary>
    public static RouteHandlerBuilder MapPatchCommand<TRequest, TCommand, TResponse>(
        this IEndpointRouteBuilder app,
        string pattern,
        Func<TRequest, TCommand> toCommand)
        where TCommand : ICommand<TResponse>
        => app.MapCommand<TRequest, TCommand, TResponse>(HttpMethods.Patch, pattern, toCommand);

    /// <summary>A DELETE endpoint for a command without a result: 204.</summary>
    public static RouteHandlerBuilder MapDeleteCommand<TRequest, TCommand>(
        this IEndpointRouteBuilder app,
        string pattern,
        Func<TRequest, TCommand> toCommand)
        where TCommand : ICommand
        => app.MapCommand<TRequest, TCommand>(HttpMethods.Delete, pattern, toCommand);

    /// <summary>A DELETE endpoint for a command with a result: 200 with it.</summary>
    public static RouteHandlerBuilder MapDeleteCommand<TRequest, TCommand, TResponse>(
        this IEndpointRouteBuilder app,
        string pattern,
        Func<TRequest, TCommand> toCommand)
        where TCommand : ICommand<TResponse>
        => app.MapCommand<TRequest, TCommand, TResponse>(HttpMethods.Delete, pattern, toCommand);

    /// <summary>An endpoint for <paramref name="method"/> and a command without a result: 204.</summary>
    public static RouteHandlerBuilder MapCommand<TRequest, TCommand>(
        this IEndpointRouteBuilder app,
        string method,
        string pattern,
        Func<TRequest, TCommand> toCommand)
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(toCommand);
        return app
            .MapMethods(
                pattern,
                [method],
                async ([AsParameters] TRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
                    await Result
                        .Create(toCommand(request))
                        .Bind(command => sender.Send(command, cancellationToken))
                        .Match(Microsoft.AspNetCore.Http.Results.NoContent, HttpResultExtensions.ToProblemDetails))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesStandardProblems();
    }

    /// <summary>An endpoint for <paramref name="method"/> and a command with a result: 200 with it.</summary>
    public static RouteHandlerBuilder MapCommand<TRequest, TCommand, TResponse>(
        this IEndpointRouteBuilder app,
        string method,
        string pattern,
        Func<TRequest, TCommand> toCommand)
        where TCommand : ICommand<TResponse>
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(toCommand);
        return app
            .MapMethods(
                pattern,
                [method],
                async ([AsParameters] TRequest request, [FromServices] ISender sender, CancellationToken cancellationToken) =>
                    await Result
                        .Create(toCommand(request))
                        .Bind(command => sender.Send(command, cancellationToken))
                        .Match(Microsoft.AspNetCore.Http.Results.Ok, HttpResultExtensions.ToProblemDetails))
            .Produces<TResponse>()
            .ProducesStandardProblems();
    }

    private static RouteHandlerBuilder ProducesStandardProblems(this RouteHandlerBuilder builder)
        => builder
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
}
