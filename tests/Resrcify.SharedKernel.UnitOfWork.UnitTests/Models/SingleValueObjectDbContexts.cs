using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Resrcify.SharedKernel.UnitOfWork.Extensions;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

internal abstract class VoDbContext(DbContextOptions options)
    : DbContext(options)
{
    internal DbSet<VoPlayer> Players { get; set; } = default!;
    internal DbSet<VoMembership> Memberships { get; set; } = default!;
    internal DbSet<VoTicket> Tickets { get; set; } = default!;
    internal DbSet<VoBadge> Badges { get; set; } = default!;
}

/// <summary>The services' way today: a hand-written <c>HasConversion</c> on every value object property.</summary>
internal sealed class ExplicitConversionDbContext(DbContextOptions options)
    : VoDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VoPlayer>(player =>
        {
            player.HasKey(x => x.Id);
            player
                .Property(x => x.Id)
                .HasConversion(x => x.Value, v => PlayerId.Create(v).Value)
                .HasMaxLength(PlayerId.MaxLength)
                .ValueGeneratedNever();
            player
                .Property(x => x.AllyCode)
                .HasConversion(x => x.Value, v => AllyCode.Create(v).Value);
            player.HasIndex(x => x.AllyCode).IsUnique();
            player
                .Property(x => x.FormerAllyCode)
                .HasConversion(x => x!.Value, v => AllyCode.Create(v).Value);
            player
                .Property(x => x.SimulatedPlayerId)
                .HasConversion(x => x != null ? x.Value : string.Empty, v => PlayerId.Create(v).Value)
                .HasMaxLength(PlayerId.MaxLength);
            player.HasIndex(x => x.SimulatedPlayerId);
            player
                .Property(x => x.Code)
                .HasConversion(x => x == null ? null : x.Value, v => v == null ? null : TrustedCode.Create(v).Value)
                .HasMaxLength(3);
            player
                .Property(x => x.Legacy)
                .HasConversion(x => x == null ? null : x.Value, v => v == null ? null : LegacyCode.Create(v).Value);
            player.OwnsOne(x => x.Address, address => address
                .Property(a => a.ForwardTo)
                .HasConversion(x => x == null ? null : x.Value, v => v == null ? null : PlayerId.Create(v).Value)
                .HasMaxLength(PlayerId.MaxLength));
            player.ComplexProperty(x => x.Stats, stats => stats
                .Property(s => s.Referrer)
                .HasConversion(x => x!.Value, v => AllyCode.Create(v).Value));
            player
                .PrimitiveCollection(x => x.Friends)
                .ElementType(friend => friend
                    .HasConversion(new ValueConverter<PlayerId, string>(
                        v => v.Value,
                        v => PlayerId.Create(v).Value))
                    .HasMaxLength(PlayerId.MaxLength));
            player
                .HasMany(x => x.Memberships)
                .WithOne()
                .HasForeignKey(x => x.PlayerId);
        });

        modelBuilder.Entity<VoMembership>(membership =>
        {
            membership.HasKey(x => new { x.Number, x.PlayerId });
            membership
                .Property(x => x.Number)
                .HasConversion(x => x.Value, v => OrderNumber.Create(v).Value)
                .ValueGeneratedNever();
            membership
                .Property(x => x.PlayerId)
                .HasConversion(x => x.Value, v => PlayerId.Create(v).Value)
                .HasMaxLength(PlayerId.MaxLength);
            membership
                .Property(x => x.TrackingId)
                .HasConversion(x => x.Value, v => TrackingId.Create(v).Value);
            membership.HasAlternateKey(x => x.TrackingId);
        });

        modelBuilder.Entity<VoTicket>(ticket =>
        {
            ticket.HasKey(x => x.Id);
            ticket
                .Property(x => x.Id)
                .HasConversion(x => x.Value, v => OrderNumber.Create(v).Value);
            ticket
                .Property(x => x.Tracking)
                .HasConversion(x => x.Value, v => TrackingId.Create(v).Value);
            ticket.HasIndex(x => x.Tracking);
        });

        modelBuilder.Entity<VoBadge>(badge =>
        {
            badge.HasKey(x => x.Id);
            badge
                .Property(x => x.Id)
                .HasConversion(x => x.Value, v => TrackingId.Create(v).Value);
        });
    }
}

/// <summary>The same model with the convention: only the lines that say something besides the conversion remain.</summary>
internal class ConventionDbContext(DbContextOptions options)
    : VoDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.AddSingleValueObjectConversions(typeof(PlayerId).Assembly);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VoPlayer>(player =>
        {
            player.HasKey(x => x.Id);
            player
                .Property(x => x.Id)
                .HasMaxLength(PlayerId.MaxLength)
                .ValueGeneratedNever();
            player.HasIndex(x => x.AllyCode).IsUnique();
            player
                .Property(x => x.SimulatedPlayerId)
                .HasMaxLength(PlayerId.MaxLength);
            player.HasIndex(x => x.SimulatedPlayerId);
            player
                .Property(x => x.Code)
                .HasMaxLength(3);
            player
                .Property(x => x.Legacy)
                .HasConversion(x => x == null ? null : x.Value, v => v == null ? null : LegacyCode.Create(v).Value);
            player.OwnsOne(x => x.Address, address => address
                .Property(a => a.ForwardTo)
                .HasMaxLength(PlayerId.MaxLength));
            player.ComplexProperty(x => x.Stats);
            player
                .PrimitiveCollection(x => x.Friends)
                .ElementType(friend => friend
                    .HasConversion(new ValueConverter<PlayerId, string>(
                        v => v.Value,
                        v => PlayerId.Create(v).Value))
                    .HasMaxLength(PlayerId.MaxLength));
            player
                .HasMany(x => x.Memberships)
                .WithOne()
                .HasForeignKey(x => x.PlayerId);
        });

        modelBuilder.Entity<VoMembership>(membership =>
        {
            membership.HasKey(x => new { x.Number, x.PlayerId });
            membership
                .Property(x => x.Number)
                .ValueGeneratedNever();
            membership
                .Property(x => x.PlayerId)
                .HasMaxLength(PlayerId.MaxLength);
            membership.HasAlternateKey(x => x.TrackingId);
        });

        modelBuilder.Entity<VoTicket>(ticket =>
        {
            ticket.HasKey(x => x.Id);
            ticket.HasIndex(x => x.Tracking);
        });

        modelBuilder.Entity<VoBadge>(badge => badge.HasKey(x => x.Id));
    }
}

/// <summary>The convention, with one property converted its own way: the property's conversion must win.</summary>
internal sealed class OverridingConventionDbContext(DbContextOptions options)
    : ConventionDbContext(options)
{
    public const string Prefix = "X";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder
            .Entity<VoPlayer>()
            .Property(x => x.SimulatedPlayerId)
            .HasConversion(x => Prefix + x!.Value, v => PlayerId.Create(v.Substring(Prefix.Length)).Value);
    }
}

/// <summary>
/// The convention's model without the owned type, for EF Core's NativeAOT compiled model. EF Core 10.0 can't build the
/// relational model of an owned type that shares its owner's table when the table has an index (the generated
/// <c>CreateRelationalModel</c> throws "Sequence contains no elements" building the index before the owner's columns
/// are mapped), with hand-written conversions as much as with the convention.
/// </summary>
internal sealed class NativeAotConventionDbContext(DbContextOptions options)
    : ConventionDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<VoPlayer>().Ignore(x => x.Address);
    }
}
