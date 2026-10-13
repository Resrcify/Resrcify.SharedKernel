using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace Resrcify.SharedKernel.UnitOfWork.Web.Extensions;

public static class OutboxAdministrationEndpointExtensions
{
    /// <summary>
    /// Maps <typeparamref name="TDbContext"/>'s outbox administration (<see cref="OutboxAdministration{TDbContext}"/>)
    /// under <paramref name="prefix"/>:
    /// <list type="bullet">
    /// <item><c>GET /</c>: what waits, retries and gave up, per event type and lane;</item>
    /// <item><c>GET /given-up?type=&amp;take=</c>: the messages that gave up, most recent first;</item>
    /// <item><c>GET /messages/{id}</c>: a message with its content and last error;</item>
    /// <item><c>POST /messages/{id}/retry</c>: tries a message that gave up again (204, or 404);</item>
    /// <item><c>POST /given-up/retry?type=</c>: tries every message that gave up again.</item>
    /// </list>
    /// The endpoints require an authenticated caller; the returned group takes the rest, such as an admin role:
    /// <c>app.MapOutboxAdministration&lt;AppDbContext&gt;().RequireAuthorization(p =&gt; p.RequireRole("Admin"))</c>.
    /// Messages hold event content: never open these to everyone.
    /// </summary>
    public static RouteGroupBuilder MapOutboxAdministration<TDbContext>(
        this IEndpointRouteBuilder app,
        string prefix = "/admin/outbox")
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(app);
        var name = $"Outbox.{typeof(TDbContext).Name}";
        var group = app
            .MapGroup(prefix)
            .RequireAuthorization()
            .WithTags("Outbox");

        group.MapGet(
                "/",
                (OutboxAdministration<TDbContext> outbox, CancellationToken cancellationToken)
                    => outbox.GetSummaryAsync(cancellationToken))
            .WithName($"{name}.Summary");

        group.MapGet(
                "/given-up",
                async (string? type, int? take, OutboxAdministration<TDbContext> outbox, CancellationToken cancellationToken) =>
                {
                    var count = take ?? 50;
                    if (count is < 1 or > OutboxAdministration<TDbContext>.MaxTake)
                        return HttpResults.Problem(
                            detail: $"take must be between 1 and {OutboxAdministration<TDbContext>.MaxTake}.",
                            statusCode: StatusCodes.Status400BadRequest);
                    return HttpResults.Ok(await outbox.ListGivenUpAsync(count, type, cancellationToken));
                })
            .WithName($"{name}.GivenUp")
            .Produces<OutboxMessageSummary[]>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet(
                "/messages/{id:guid}",
                async (Guid id, OutboxAdministration<TDbContext> outbox, CancellationToken cancellationToken)
                    => await outbox.FindAsync(id, cancellationToken) is { } message
                        ? HttpResults.Ok(message)
                        : HttpResults.NotFound())
            .WithName($"{name}.Message")
            .Produces<OutboxMessageDetails>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost(
                "/messages/{id:guid}/retry",
                async (Guid id, OutboxAdministration<TDbContext> outbox, CancellationToken cancellationToken)
                    => await outbox.RetryAsync(id, cancellationToken)
                        ? HttpResults.NoContent()
                        : HttpResults.NotFound())
            .WithName($"{name}.Retry")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost(
                "/given-up/retry",
                async (string? type, OutboxAdministration<TDbContext> outbox, CancellationToken cancellationToken)
                    => new OutboxRetried(await outbox.RetryAllGivenUpAsync(type, cancellationToken)))
            .WithName($"{name}.RetryAll");

        return group;
    }
}

/// <summary>How many messages that gave up were tried again.</summary>
public sealed record OutboxRetried(int Retried);
