using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

namespace Resrcify.SharedKernel.UnitOfWork.Interceptors;

/// <summary>Sets <c>CreatedOnUtc</c> / <c>ModifiedOnUtc</c> from <paramref name="timeProvider"/> (the system clock by default).</summary>
public sealed class UpdateAuditableEntitiesInterceptor(TimeProvider? timeProvider = null)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            UpdateAuditableEntities(
                eventData,
                eventData.Context);
        return base.SavingChanges(
            eventData,
            result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            UpdateAuditableEntities(
                eventData,
                eventData.Context);
        return await base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }
    private void UpdateAuditableEntities(
        DbContextEventData saving,
        DbContext context)
    {
        DateTime utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        List<EntityEntry<IAuditableEntity>> entries = SaveChangesEntries.Where<IAuditableEntity>(
            saving,
            context,
            entry => entry.State is EntityState.Added or EntityState.Modified);

        foreach (EntityEntry<IAuditableEntity> entityEntry in entries)
        {
            if (entityEntry.State == EntityState.Added)
            {
                SetCurrentPropertyValue(
                    entityEntry,
                    nameof(IAuditableEntity.CreatedOnUtc),
                    utcNow);
                SetCurrentPropertyValue(
                    entityEntry,
                    nameof(IAuditableEntity.ModifiedOnUtc),
                    utcNow);
            }

            if (entityEntry.State == EntityState.Modified)
            {
                SetCurrentPropertyValue(
                    entityEntry,
                    nameof(IAuditableEntity.ModifiedOnUtc),
                    utcNow);
            }
        }
    }
    private static void SetCurrentPropertyValue(
        EntityEntry entry,
        string propertyName,
        DateTime utcNow)
        => entry.Property(propertyName).CurrentValue = utcNow;
}