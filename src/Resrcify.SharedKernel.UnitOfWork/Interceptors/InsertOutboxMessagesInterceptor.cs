using System;
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
        var outboxMessages = context.ChangeTracker
            .Entries<IAggregateRoot>()
            .Select(x => x.Entity)
            .SelectMany(aggregateRoot =>
            {
                var domainEvents = aggregateRoot.GetDomainEvents();

                aggregateRoot.ClearDomainEvents();

                return domainEvents;
            })
            .Select(domainEvent => new OutboxMessage
            {
                Id = Guid.NewGuid(),
                OccurredOnUtc = DateTime.UtcNow,
                Type = domainEvent.GetType().FullName!,
                Content = serializer.Serialize(domainEvent)
            })
            .ToList();

        await context
            .Set<OutboxMessage>()
            .AddRangeAsync(
                outboxMessages,
                cancellationToken);
    }
}
