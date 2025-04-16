using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;

namespace Resrcify.SharedKernel.UnitOfWork.Interceptors;

public sealed class UpdateDeletableEntitiesInterceptor
    : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            UpdateDeletableEntities(eventData.Context);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
    private static void UpdateDeletableEntities(DbContext context)
    {
        IEnumerable<EntityEntry<IDeletableEntity>> entries =
            context
                .ChangeTracker
                .Entries<IDeletableEntity>()
                .Where(e => e.State == EntityState.Deleted);

        foreach (EntityEntry<IDeletableEntity> entityEntry in entries)
        {
            entityEntry.State = EntityState.Modified;
            entityEntry.Property(a => a.DeletedOnUtc)
                .CurrentValue = DateTime.UtcNow;
            entityEntry.Property(a => a.IsDeleted)
                .CurrentValue = true;
        }
    }
}