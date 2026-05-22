using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options)
    : DbContext(options)
{
    /// <summary>Non-public schema the outbox table is mapped into for these tests.</summary>
    internal const string OutboxSchema = "messaging";

    internal DbSet<TestAggregate> Aggregates => Set<TestAggregate>();
    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestAggregate>(b =>
        {
            b.ToTable("Aggregates");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(64).IsRequired();
        });

        // Outbox table placed in a dedicated (non-public) schema, with a
        // PostgreSQL-tuned index: covering INCLUDE columns plus a partial filter
        // so only unprocessed rows are indexed.
        modelBuilder.ApplyOutboxMessageConfiguration(
            schema: OutboxSchema,
            configureUnprocessedIndex: index => index
                .IncludeProperties("Type", "Content")
                .HasFilter("\"ProcessedOnUtc\" IS NULL"));
    }
}
