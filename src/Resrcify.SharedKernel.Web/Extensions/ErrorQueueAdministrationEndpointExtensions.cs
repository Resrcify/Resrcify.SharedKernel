using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class ErrorQueueAdministrationEndpointExtensions
{
    /// <summary>The most messages one listing shows.</summary>
    public const int MaxTake = 1000;

    /// <summary>
    /// Maps the service's message bus error queue (<see cref="IErrorQueue"/>, from <c>AddMessageBus</c>): the
    /// integration events whose handlers kept failing, under <paramref name="prefix"/>:
    /// <list type="bullet">
    /// <item><c>GET /?take=</c>: the first messages, oldest first, with why each failed, and how many there are;</item>
    /// <item><c>DELETE /messages/{id}</c>: deletes a message (204, or 404);</item>
    /// <item><c>DELETE /?type=</c>: deletes every message, or every one of the type;</item>
    /// <item><c>POST /messages/{id}/retry</c>: moves a message back to the queue it failed on, to be handled again
    /// (204, 404, or 409 when it can't be moved);</item>
    /// <item><c>POST /retry?type=</c>: moves every message, or every one of the type, back.</item>
    /// </list>
    /// The endpoints require an authenticated caller; the returned group takes the rest, such as an admin role:
    /// <c>app.MapErrorQueueAdministration().RequireAuthorization(p =&gt; p.RequireRole("Admin"))</c>. Messages hold
    /// event content: never open these to everyone.
    /// </summary>
    public static RouteGroupBuilder MapErrorQueueAdministration(
        this IEndpointRouteBuilder app,
        string prefix = "/admin/error-queue")
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app
            .MapGroup(prefix)
            .RequireAuthorization()
            .WithTags("Message bus");

        group.MapGet(
                "/",
                async (int? take, IErrorQueue errorQueue, CancellationToken cancellationToken) =>
                {
                    var count = take ?? 100;
                    if (count is < 1 or > MaxTake)
                        return HttpResults.Problem(
                            detail: $"take must be between 1 and {MaxTake}.",
                            statusCode: StatusCodes.Status400BadRequest);
                    var listed = await errorQueue.ListAsync(count, cancellationToken);
                    return listed.IsSuccess
                        ? HttpResults.Ok(listed.Value)
                        : listed.ToProblemDetails();
                })
            .WithName("ErrorQueue.List")
            .Produces<ErrorQueueContents>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete(
                "/messages/{id}",
                async (string id, IErrorQueue errorQueue, CancellationToken cancellationToken) =>
                {
                    var deleted = await errorQueue.DeleteAsync(id, cancellationToken);
                    return deleted.IsSuccess
                        ? HttpResults.NoContent()
                        : deleted.ToProblemDetails();
                })
            .WithName("ErrorQueue.Delete")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete(
                "/",
                async (string? type, IErrorQueue errorQueue, CancellationToken cancellationToken) =>
                {
                    var deleted = await errorQueue.DeleteAllAsync(type, cancellationToken);
                    return deleted.IsSuccess
                        ? HttpResults.Ok(new ErrorQueueDeleted(deleted.Value))
                        : deleted.ToProblemDetails();
                })
            .WithName("ErrorQueue.DeleteAll")
            .Produces<ErrorQueueDeleted>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost(
                "/messages/{id}/retry",
                async (string id, IErrorQueue errorQueue, CancellationToken cancellationToken) =>
                {
                    var retried = await errorQueue.RetryAsync(id, cancellationToken);
                    return retried.IsSuccess
                        ? HttpResults.NoContent()
                        : retried.ToProblemDetails();
                })
            .WithName("ErrorQueue.Retry")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost(
                "/retry",
                async (string? type, IErrorQueue errorQueue, CancellationToken cancellationToken) =>
                {
                    var retried = await errorQueue.RetryAllAsync(type, cancellationToken);
                    return retried.IsSuccess
                        ? HttpResults.Ok(new ErrorQueueRetried(retried.Value))
                        : retried.ToProblemDetails();
                })
            .WithName("ErrorQueue.RetryAll")
            .Produces<ErrorQueueRetried>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }
}

/// <summary>How many messages were deleted from the error queue.</summary>
public sealed record ErrorQueueDeleted(int Deleted);

/// <summary>How many messages were moved back from the error queue, to be handled again.</summary>
public sealed record ErrorQueueRetried(int Retried);
