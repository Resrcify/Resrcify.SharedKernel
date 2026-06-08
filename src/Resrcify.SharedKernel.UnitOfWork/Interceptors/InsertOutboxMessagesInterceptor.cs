using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Interceptors;

public sealed class InsertOutboxMessagesInterceptor(
    IOutboxSerializer serializer,
    IOutboxInsertStrategy? insertStrategy = null)
    : SaveChangesInterceptor
{
    private readonly IOutboxInsertStrategy _insertStrategy = insertStrategy ?? new DefaultOutboxInsertStrategy();

    private static readonly ConditionalWeakTable<DbContext, IDbContextTransaction> _ourTransactions = new();

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
            return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);

        if (eventData.Context.Database.CurrentTransaction is null)
        {
            var tx = await eventData.Context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            _ourTransactions.AddOrUpdate(eventData.Context, tx);
        }

        await ConvertDomainEventsToOutboxMessages(eventData.Context, cancellationToken)
            .ConfigureAwait(false);

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null && _ourTransactions.TryGetValue(eventData.Context, out var tx))
        {
            _ourTransactions.Remove(eventData.Context);
            try
            {
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await tx.DisposeAsync().ConfigureAwait(false);
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override async Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null && _ourTransactions.TryGetValue(eventData.Context, out var tx))
        {
            _ourTransactions.Remove(eventData.Context);
            try
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Swallow: surfacing rollback errors would mask the original SaveChanges exception.
            }
            finally
            {
                await tx.DisposeAsync().ConfigureAwait(false);
            }
        }

        await base.SaveChangesFailedAsync(eventData, cancellationToken).ConfigureAwait(false);
    }

    private async Task ConvertDomainEventsToOutboxMessages(
        DbContext context,
        CancellationToken cancellationToken)
    {
        var domainEvents = context.ChangeTracker
            .Entries<IAggregateRoot>()
            .Select(x => x.Entity)
            .SelectMany(aggregateRoot =>
            {
                var events = aggregateRoot.GetDomainEvents();
                aggregateRoot.ClearDomainEvents();
                return events;
            })
            .ToList();

        if (domainEvents.Count == 0)
            return;

        var outboxMessages = new List<OutboxMessage>(domainEvents.Count);
        var dedupKeysSeen = new HashSet<string>(StringComparer.Ordinal);
        var now = DateTime.UtcNow;

        foreach (var domainEvent in domainEvents)
        {
            string? dedupKey = null;
            if (domainEvent is IDedupable dedupable)
            {
                dedupKey = $"{domainEvent.GetType().FullName}:{dedupable.DedupKey}";

                if (!dedupKeysSeen.Add(dedupKey))
                    continue;
            }

            outboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                OccurredOnUtc = now,
                Type = domainEvent.GetType().FullName!,
                Content = serializer.Serialize(domainEvent),
                DedupKey = dedupKey
            });
        }

        // Race-tolerant, not race-proof: concurrent transactions from different DbContexts
        // can each pass this check and both insert. Pair with PostgresOnConflictOutboxInsertStrategy
        // + a partial UNIQUE index for strict uniqueness.
        if (dedupKeysSeen.Count > 0)
        {
            var keysToCheck = dedupKeysSeen.ToList();

            var existingKeys = await context
                .Set<OutboxMessage>()
                .Where(m => m.ProcessedOnUtc == null
                    && m.DedupKey != null
                    && keysToCheck.Contains(m.DedupKey))
                .Select(m => m.DedupKey!)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (existingKeys.Count > 0)
            {
                var existingKeySet = new HashSet<string>(existingKeys, StringComparer.Ordinal);
                outboxMessages = outboxMessages
                    .Where(m => m.DedupKey is null || !existingKeySet.Contains(m.DedupKey))
                    .ToList();
            }
        }

        if (outboxMessages.Count == 0)
            return;

        await _insertStrategy
            .InsertAsync(context, outboxMessages, cancellationToken)
            .ConfigureAwait(false);
    }
}
