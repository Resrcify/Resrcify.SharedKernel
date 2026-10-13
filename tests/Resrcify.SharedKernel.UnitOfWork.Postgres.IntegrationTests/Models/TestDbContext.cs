using System;
using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options)
    : DbContext(options)
{
    internal DbSet<Shard> Shards => Set<Shard>();

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shard>(shard =>
        {
            shard.ToTable("Shards");
            shard.HasKey(x => x.Id);
            shard.Property(x => x.Name).HasMaxLength(64).IsRequired();
            shard.HasIndex(x => x.Name).IsUnique();
            shard.HasPostgresRowVersion();
        });

        modelBuilder.ApplyOutboxMessageConfiguration(
            configureUnprocessedIndex: PostgresOutboxIndexes.Instance.PartialUnprocessed,
            configureDedupIndex: PostgresOutboxIndexes.Instance.PartialUniqueDedup);
    }
}

internal sealed class Shard
    : AggregateRoot<Guid>, IAuditableEntity
{
    public Shard(Guid id, string name)
        : base(id)
        => Name = name;

    public string Name { get; private set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime ModifiedOnUtc { get; set; }

    public void Rename(string name)
    {
        Name = name;
        RaiseDomainEvent(new ShardRenamed(Guid.NewGuid(), Id, name));
    }

    public void RequestRanks()
        => RaiseDomainEvent(new RanksRequested(Guid.NewGuid(), Id));
}

internal sealed record ShardRenamed(
    Guid Id,
    Guid ShardId,
    string Name)
    : DomainEvent(Id);

/// <summary>An event the tests put in an outbox lane.</summary>
internal sealed record RanksRequested(
    Guid Id,
    Guid ShardId)
    : DomainEvent(Id);
