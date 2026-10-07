using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;

internal sealed record OutboxMessageToProcess(
    Guid Id,
    string Type,
    string Content,
    int RetryCount,
    DateTime OccurredOnUtc);

/// <summary>How processing one outbox message ended.</summary>
internal enum OutboxProcessOutcome
{
    /// <summary>Published and marked processed.</summary>
    Processed,

    /// <summary>A handler failed: the failure is recorded and the message retried later.</summary>
    Failed,

    /// <summary>Another instance holds the message (the claim failed): left alone.</summary>
    Skipped,
}

/// <summary>
/// Processes one outbox message: publish it and mark it processed in one transaction, or record the
/// failure so it is retried. Shared by the regular outbox job and the outbox lanes.
/// </summary>
internal static class OutboxMessageProcessor<TDbContext>
    where TDbContext : DbContext
{
    /// <param name="claim">
    /// Optional row claim, run first inside the transaction. Returning <see langword="false"/> means
    /// another instance holds the message: it is left alone, neither processed nor failed.
    /// </param>
    /// <param name="maxRetryCount">The tries a message gets: a failure on the last one is counted as giving up.</param>
    public static async Task<OutboxProcessOutcome> ProcessAsync(
        IServiceScopeFactory scopeFactory,
        OutboxMessageToProcess message,
        IOutboxLaneClaim? claim,
        int maxRetryCount,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        var claimed = true;
        TimeProvider time;
        long started;
        using var activity = OutboxDiagnostics.ActivitySource.StartActivity("outbox process", ActivityKind.Internal);
        activity?.SetTag("outbox.message.id", message.Id);
        activity?.SetTag("outbox.message.retry_count", message.RetryCount);

        // A dedicated DI scope per message: the DbContext, the event handlers and the
        // "mark processed" update are isolated to this message and this transaction.
        // Nothing here touches a DbContext (or its change tracker) shared with other
        // work, so there is nothing it can clobber by mistake.
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            time = provider.GetService<TimeProvider>() ?? TimeProvider.System;
            started = time.GetTimestamp();
            var context = provider.GetRequiredService<TDbContext>();
            // This context's own unit of work, not whichever IUnitOfWork DI resolves (with two DbContexts, that may be
            // the other one's). Handlers that resolve IUnitOfWork for this context join its transaction on a savepoint.
            using var unitOfWork = new Primitives.UnitOfWork<TDbContext>(context);
            var publisher = provider.GetRequiredService<IPublisher>();
            var serializer = provider.GetRequiredService<IOutboxSerializer>();

            try
            {
                // Publishing the event and marking the message processed commit
                // together: the message is only marked done if everything its
                // handlers persisted commits as well.
                await unitOfWork.ExecuteInTransactionAsync(
                    async token =>
                    {
                        if (claim is not null && !await claim.TryClaimAsync(context, message.Id, token))
                        {
                            claimed = false;
                            return;
                        }

                        var domainEvent = serializer.Deserialize(message.Content)
                            ?? throw new InvalidOperationException(
                                "Outbox message content could not be deserialized.");
                        if (activity is not null)
                        {
                            activity.DisplayName = $"outbox {domainEvent.GetType().Name}";
                            activity.SetTag("outbox.message.type", domainEvent.GetType().FullName);
                        }

                        // Handlers can take IDs that are the same on every attempt (OutboxMessageContext).
                        using (OutboxMessageContext.Enter(message.Id))
                            await publisher.Publish(domainEvent, token);

                        MarkProcessed(context, message.Id, time.GetUtcNow().UtcDateTime);
                    },
                    cancellationToken: cancellationToken);
            }
            // Only a shutdown is exempt: a handler's own cancellation (e.g. an HTTP timeout) is a failure like any other,
            // so the message is retried and, if it keeps failing, becomes poison instead of blocking the outbox.
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failure = exception;
                activity?.AddException(exception);
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            }
        }

        if (failure is not null)
        {
            var givesUp = message.RetryCount + 1 >= maxRetryCount;
            await RecordFailureAsync(scopeFactory, message, failure, maxRetryCount, time, cancellationToken);
            Record(message, givesUp ? "gave_up" : "retrying", time.GetElapsedTime(started), wait: null);
            return OutboxProcessOutcome.Failed;
        }
        if (!claimed)
            return OutboxProcessOutcome.Skipped;
        Record(message, "processed", time.GetElapsedTime(started), time.GetUtcNow().UtcDateTime - message.OccurredOnUtc);
        return OutboxProcessOutcome.Processed;
    }

    private static void Record(OutboxMessageToProcess message, string outcome, TimeSpan elapsed, TimeSpan? wait)
        => OutboxDiagnostics.RecordHandled(
            typeof(TDbContext).Name,
            OutboxDiagnostics.EventName(message.Type),
            outcome,
            elapsed,
            wait);

    // Marks the row processed through the change tracker (a stub entity, no load
    // required) so the UPDATE participates in the same transaction and SaveChanges
    // as the work the event handlers persisted.
    private static void MarkProcessed(TDbContext context, Guid id, DateTime processedOnUtc)
    {
        var stub = new OutboxMessage { Id = id };
        context.Attach(stub);
        stub.ProcessedOnUtc = processedOnUtc;
    }

    // Records the failure in a fresh scope (the processing scope was disposed along
    // with its rolled-back transaction and the failed handler's tracked entities).
    // The row stays unprocessed and is retried, until its last try fails: then it is
    // marked given up (OutboxMessage.GivenUpProcessedOnUtc), which takes it out of the
    // unprocessed rows the polls read.
    //
    // One statement, counted in the database: the claim's lock is gone by now (its
    // transaction rolled back), so another instance may be trying the same message.
    // Counting from this try's snapshot would lose a try, and an unconditional update
    // could mark given up a message the other instance has just published.
    private static async Task RecordFailureAsync(
        IServiceScopeFactory scopeFactory,
        OutboxMessageToProcess message,
        Exception exception,
        int maxRetryCount,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var error = exception.ToString();
        var givenUpError = string.Create(
            CultureInfo.InvariantCulture,
            $"Gave up at {time.GetUtcNow().UtcDateTime:O} after {maxRetryCount} tries. {error}");
        var givenUpOn = OutboxMessage.GivenUpProcessedOnUtc;

        await context
            .Set<OutboxMessage>()
            .Where(row => row.Id == message.Id && row.ProcessedOnUtc == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.RetryCount, row => row.RetryCount + 1)
                    .SetProperty(row => row.ProcessedOnUtc, row => row.RetryCount + 1 >= maxRetryCount ? givenUpOn : null)
                    .SetProperty(row => row.Error, row => row.RetryCount + 1 >= maxRetryCount ? givenUpError : error),
                cancellationToken);
    }
}
