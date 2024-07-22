using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

namespace Resrcify.SharedKernel.UnitOfWork.Interceptors;

public sealed class UpdateAuditableEntitiesInterceptor : SaveChangesInterceptor
{
    public async override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            UpdateAuditableEntities(eventData.Context);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
    private static void UpdateAuditableEntities(DbContext context)
    {
        DateTime utcNow = DateTime.UtcNow;
        IEnumerable<EntityEntry<IAuditableEntity>> entries =
            context
                .ChangeTracker
                .Entries<IAuditableEntity>();

        foreach (EntityEntry<IAuditableEntity> entityEntry in entries)
        {
            if (entityEntry.State == EntityState.Added)
            {
                SetCurrentPropertyValue(
                    entityEntry, nameof(IAuditableEntity.CreatedOnUtc), utcNow);
                SetCurrentPropertyValue(
                    entityEntry, nameof(IAuditableEntity.ModifiedOnUtc), utcNow);
            }

            if (entityEntry.State == EntityState.Modified)
            {
                SetCurrentPropertyValue(
                    entityEntry, nameof(IAuditableEntity.ModifiedOnUtc), utcNow);
            }
        }
    }
    private static void SetCurrentPropertyValue(
        EntityEntry entry,
        string propertyName,
        DateTime utcNow)
        => entry.Property(propertyName).CurrentValue = utcNow;
}