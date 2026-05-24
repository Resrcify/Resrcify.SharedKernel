using System;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Abstractions;

/// <summary>
/// Provider-specific index configurators paired with an
/// <see cref="IOutboxInsertStrategy"/>. The actions are passed to
/// <c>OutboxMessageConfiguration</c>'s <c>configureUnprocessedIndex</c> /
/// <c>configureDedupIndex</c> hooks. One implementation per database
/// provider (PostgreSQL, SQL Server, etc.) ships alongside its matching
/// insert strategy so the index shape and the strategy's conflict clause
/// stay in lock-step.
/// </summary>
public interface IOutboxIndexes
{
    /// <summary>
    /// Restricts the unprocessed-messages index so it stays small. Typically a
    /// partial filter on <c>ProcessedOnUtc IS NULL</c> for providers that
    /// support partial indexes; a no-op for providers that don't.
    /// </summary>
    Action<IndexBuilder<OutboxMessage>> PartialUnprocessed { get; }

    /// <summary>
    /// Promotes the dedup index to whatever shape the paired
    /// <see cref="IOutboxInsertStrategy"/> needs in order to swallow concurrent
    /// duplicate inserts without throwing — typically a strict partial UNIQUE
    /// on <c>DedupKey</c> for partial-index providers.
    /// </summary>
    Action<IndexBuilder<OutboxMessage>> PartialUniqueDedup { get; }
}
