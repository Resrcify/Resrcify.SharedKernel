using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Resrcify.SharedKernel.UnitOfWork.Interceptors;

/// <summary>
/// Reads the change tracker for the save interceptors, detecting changes once per save instead of once per interceptor.
/// </summary>
/// <remarks>
/// <para>
/// <c>ChangeTracker.Entries&lt;T&gt;()</c> runs a full <c>DetectChanges</c> (a scan of every tracked entity) each time it
/// is called, and EF runs one more after the interceptors: with three interceptors a save scanned the change tracker
/// four times. Here the first interceptor of a save detects changes, and every interceptor then reads the entries with
/// automatic detection off (restored afterwards), so a save scans twice: once here and once in EF.
/// </para>
/// <para>
/// A save is recognised by its <see cref="DbContextEventData"/>: EF makes one per <c>SaveChanges</c> call and hands the
/// same instance to every interceptor. Nothing has to be cleared when the save ends, fails, is cancelled or an
/// interceptor throws: the next save has event data of its own and detects again.
/// </para>
/// <para>
/// Changes an interceptor makes through the entries (state, property values) are tracked as they are made, so the
/// interceptors after it see them without detecting again. With <c>AutoDetectChangesEnabled</c> off, nothing is
/// detected, as EF itself does then.
/// </para>
/// </remarks>
internal static class SaveChangesEntries
{
    private static readonly ConditionalWeakTable<DbContextEventData, object> DetectedSaves = new();
    private static readonly object Detected = new();

    /// <summary>The tracked entries of <typeparamref name="TEntity"/> that match <paramref name="predicate"/>.</summary>
    public static List<EntityEntry<TEntity>> Where<TEntity>(
        DbContextEventData saving,
        DbContext context,
        Func<EntityEntry<TEntity>, bool> predicate)
        where TEntity : class
    {
        var changeTracker = context.ChangeTracker;
        if (!changeTracker.AutoDetectChangesEnabled)
            return Collect(changeTracker, predicate);

        if (DetectedSaves.TryAdd(saving, Detected))
            changeTracker.DetectChanges();

        changeTracker.AutoDetectChangesEnabled = false;
        try
        {
            return Collect(changeTracker, predicate);
        }
        finally
        {
            changeTracker.AutoDetectChangesEnabled = true;
        }
    }

    private static List<EntityEntry<TEntity>> Collect<TEntity>(
        ChangeTracker changeTracker,
        Func<EntityEntry<TEntity>, bool> predicate)
        where TEntity : class
        => [.. changeTracker
            .Entries<TEntity>()
            .Where(predicate)];
}
