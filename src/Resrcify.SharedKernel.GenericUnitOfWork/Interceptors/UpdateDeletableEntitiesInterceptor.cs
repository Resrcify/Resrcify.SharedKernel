using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

namespace Resrcify.SharedKernel.GenericUnitOfWork.Interceptors;

public sealed class UpdateDeletableEntitiesInterceptor : SaveChangesInterceptor
{
    public async override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            UpdateDeletableEntities(eventData.Context);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
    private static void UpdateDeletableEntities(DbContext context)
    {
        DateTime utcNow = DateTime.UtcNow;
        IEnumerable<EntityEntry<IDeletableEntity>> entries =
            context
                .ChangeTracker
                .Entries<IDeletableEntity>();

        foreach (EntityEntry<IDeletableEntity> entityEntry in entries)
        {
            if (entityEntry.State == EntityState.Deleted)
            {
                entityEntry.Property(a => a.DeletedOnUtc)
                    .CurrentValue = DateTime.UtcNow;
                entityEntry.Property(a => a.IsDeleted)
                    .CurrentValue = true;
            }
        }
    }
}