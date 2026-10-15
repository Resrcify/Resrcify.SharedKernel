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

    internal DbSet<Member> Members => Set<Member>();

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

        modelBuilder.Entity<Member>(member =>
        {
            member.ToTable("Members", table => table.HasCheckConstraint(Member.RankConstraint, "\"Rank\" >= 0"));
            member.HasKey(x => x.Id);
            member.Property(x => x.Nickname).HasMaxLength(Member.NicknameMaxLength).IsRequired();
            member.HasOne<Shard>()
                .WithMany()
                .HasForeignKey(x => x.ShardId)
                .HasConstraintName(Member.ShardConstraint)
                .OnDelete(DeleteBehavior.Restrict);
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

/// <summary>
/// A row with every constraint a save can break: a required, length-limited <see cref="Nickname"/>, a
/// <see cref="Rank"/> checked to be at least 0, and a foreign key to its shard.
/// </summary>
internal sealed class Member(
    Guid id,
    Guid shardId,
    string? nickname,
    int rank)
{
    public const string RankConstraint = "CK_Members_Rank";
    public const string ShardConstraint = "FK_Members_Shards_ShardId";
    public const int NicknameMaxLength = 16;

    public Guid Id { get; private set; } = id;

    public Guid ShardId { get; private set; } = shardId;

    // Nullable in the model so a test can leave it out; the column is NOT NULL.
    public string? Nickname { get; private set; } = nickname;

    public int Rank { get; private set; } = rank;
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
