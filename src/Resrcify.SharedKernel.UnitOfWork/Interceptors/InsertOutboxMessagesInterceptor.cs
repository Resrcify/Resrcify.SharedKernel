using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Interceptors;

/// <summary>
/// Captures domain events raised by aggregate roots and writes them to the outbox
/// in the same transaction as the business change. The JSON strategy is supplied
/// by the injected <see cref="IOutboxSerializer"/>.
/// <para>
/// Events implementing <see cref="IDedupable"/> get a composed dedup key
/// (<c>{event.GetType().FullName}:{IDedupable.DedupKey}</c>). Within the current
/// SaveChanges batch, only the first event per dedup key is kept. Against existing
/// outbox rows, a pre-check skips inserts when a row with the same key is already
/// unprocessed. The pre-check is race-tolerant but not race-proof: concurrent
/// transactions from different <see cref="DbContext"/> instances can each pass the
/// pre-check and both insert. See <see cref="OutboxMessageConfiguration"/> for how
/// to add a strict partial unique index at the application level.
/// </para>
/// </summary>
public sealed class InsertOutboxMessagesInterceptor(IOutboxSerializer serializer)
    : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            await ConvertDomainEventsToOutboxMessages(
                eventData.Context,
                cancellationToken);
        return await base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
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

        // Build the outbox messages, computing dedup keys for IDedupable events.
        // In-batch dedup: only the first event per dedup key in this SaveChanges
        // becomes an outbox row.
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

        // Cross-batch dedup: skip any dedupable row that already has an unprocessed
        // sibling in the table. Best-effort — concurrent transactions can race past
        // this; see XML doc for the strict-uniqueness opt-in.
        if (dedupKeysSeen.Count > 0)
        {
            var keysToCheck = dedupKeysSeen.ToList();

            var existingKeys = await context
                .Set<OutboxMessage>()
                .Where(m => m.ProcessedOnUtc == null
                    && m.DedupKey != null
                    && keysToCheck.Contains(m.DedupKey))
                .Select(m => m.DedupKey!)
                .ToListAsync(cancellationToken);

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

        await context
            .Set<OutboxMessage>()
            .AddRangeAsync(outboxMessages, cancellationToken);
    }
}
