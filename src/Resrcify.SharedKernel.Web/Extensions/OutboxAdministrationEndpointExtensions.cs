using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class OutboxAdministrationEndpointExtensions
{
    /// <summary>The <c>take</c> of <c>GET /given-up</c> when none is given.</summary>
    public const int DefaultTake = 50;

    /// <summary>
    /// Maps <typeparamref name="TContext"/>'s outbox administration (<see cref="IOutboxAdministration{TContext}"/>,
    /// registered with the outbox by the unit of work package) under <paramref name="prefix"/>:
    /// <list type="bullet">
    /// <item><c>GET /</c>: what waits, retries and gave up, per event type and lane;</item>
    /// <item><c>GET /given-up?type=&amp;take=</c>: the messages that gave up, most recent first;</item>
    /// <item><c>GET /messages/{id}</c>: a message with its content and last error;</item>
    /// <item><c>POST /messages/{id}/retry</c>: tries a message that gave up again (204, or 404);</item>
    /// <item><c>POST /given-up/retry?type=</c>: tries every message that gave up again;</item>
    /// <item><c>DELETE /messages/{id}</c>: deletes a message that gave up, for good (204, or 404);</item>
    /// <item><c>DELETE /given-up?type=</c>: deletes every message that gave up, or every one of the type.</item>
    /// </list>
    /// The endpoints require an authenticated caller; the returned group takes the rest, such as an admin role:
    /// <c>app.MapOutboxAdministration&lt;AppDbContext&gt;().RequireAuthorization(p =&gt; p.RequireRole("Admin"))</c>.
    /// Messages hold event content: never open these to everyone.
    /// </summary>
    public static RouteGroupBuilder MapOutboxAdministration<TContext>(
        this IEndpointRouteBuilder app,
        string prefix = "/admin/outbox")
        where TContext : class
    {
        ArgumentNullException.ThrowIfNull(app);
        var name = $"Outbox.{typeof(TContext).Name}";
        var group = app
            .MapGroup(prefix)
            .RequireAuthorization()
            .WithTags("Outbox");

        group.MapGet(
                "/",
                (IOutboxAdministration<TContext> outbox, CancellationToken cancellationToken)
                    => outbox.GetSummaryAsync(cancellationToken))
            .WithName($"{name}.Summary");

        group.MapGet(
                "/given-up",
                async (string? type, int? take, IOutboxAdministration<TContext> outbox, CancellationToken cancellationToken) =>
                {
                    var count = take ?? DefaultTake;
                    if (count is < 1 or > IOutboxAdministration<TContext>.MaxTake)
                        return HttpResults.Problem(
                            detail: $"take must be between 1 and {IOutboxAdministration<TContext>.MaxTake}.",
                            statusCode: StatusCodes.Status400BadRequest);
                    return HttpResults.Ok(await outbox.ListGivenUpAsync(count, type, cancellationToken));
                })
            .WithName($"{name}.GivenUp")
            .Produces<OutboxMessageSummary[]>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet(
                "/messages/{id:guid}",
                async (Guid id, IOutboxAdministration<TContext> outbox, CancellationToken cancellationToken)
                    => await outbox.FindAsync(id, cancellationToken) is { } message
                        ? HttpResults.Ok(message)
                        : HttpResults.NotFound())
            .WithName($"{name}.Message")
            .Produces<OutboxMessageDetails>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost(
                "/messages/{id:guid}/retry",
                async (Guid id, IOutboxAdministration<TContext> outbox, CancellationToken cancellationToken)
                    => await outbox.RetryAsync(id, cancellationToken)
                        ? HttpResults.NoContent()
                        : HttpResults.NotFound())
            .WithName($"{name}.Retry")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost(
                "/given-up/retry",
                async (string? type, IOutboxAdministration<TContext> outbox, CancellationToken cancellationToken)
                    => new OutboxRetried(await outbox.RetryAllGivenUpAsync(type, cancellationToken)))
            .WithName($"{name}.RetryAll");

        group.MapDelete(
                "/messages/{id:guid}",
                async (Guid id, IOutboxAdministration<TContext> outbox, CancellationToken cancellationToken)
                    => await outbox.DeleteAsync(id, cancellationToken)
                        ? HttpResults.NoContent()
                        : HttpResults.NotFound())
            .WithName($"{name}.Delete")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete(
                "/given-up",
                async (string? type, IOutboxAdministration<TContext> outbox, CancellationToken cancellationToken)
                    => new OutboxDeleted(await outbox.DeleteAllGivenUpAsync(type, cancellationToken)))
            .WithName($"{name}.DeleteAll");

        return group;
    }
}

/// <summary>How many messages that gave up were tried again.</summary>
public sealed record OutboxRetried(int Retried);

/// <summary>How many messages that gave up were deleted.</summary>
public sealed record OutboxDeleted(int Deleted);
