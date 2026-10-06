using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions;

/// <summary>
/// Optimistic concurrency on PostgreSQL without a column of its own: the row's <c>xmin</c> system column, which
/// PostgreSQL changes on every update, is the concurrency token. A save that updates or deletes a row someone else
/// changed since it was read fails with <c>DbUpdateConcurrencyException</c> (<c>TryCompleteAsync</c> returns it as
/// <c>PersistenceErrors.Concurrency</c>, a <c>Conflict</c>).
/// </summary>
/// <remarks>
/// Mapped as Npgsql recommends: a <see cref="uint"/> property, <c>IsRowVersion()</c>, on the <c>xmin</c> column of type
/// <c>xid</c>. Migrations don't create it (it is a system column every table has), so adding it needs no schema
/// change; the model snapshot changes, so add a (empty) migration.
/// </remarks>
public static class PostgresRowVersionExtensions
{
    /// <summary>The system column the row version is read from.</summary>
    public const string Column = "xmin";

    /// <summary>
    /// Makes <c>xmin</c> the entity's concurrency token through a shadow property (named <paramref name="propertyName"/>),
    /// so the entity class doesn't change.
    /// </summary>
    /// <example><c>builder.HasPostgresRowVersion();</c> in an <c>IEntityTypeConfiguration&lt;Shard&gt;</c>.</example>
    public static PropertyBuilder<uint> HasPostgresRowVersion<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string propertyName = Column)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return MapToXmin(builder.Property<uint>(propertyName));
    }

    /// <summary>Makes <c>xmin</c> the entity's concurrency token through <paramref name="property"/> (a <see cref="uint"/>).</summary>
    /// <example><c>builder.HasPostgresRowVersion(shard =&gt; shard.Version);</c></example>
    public static PropertyBuilder<uint> HasPostgresRowVersion<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, uint>> property)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(property);
        return MapToXmin(builder.Property(property));
    }

    private static PropertyBuilder<uint> MapToXmin(PropertyBuilder<uint> property)
        => property
            .IsRowVersion()
            .HasColumnName(Column)
            .HasColumnType("xid");
}
