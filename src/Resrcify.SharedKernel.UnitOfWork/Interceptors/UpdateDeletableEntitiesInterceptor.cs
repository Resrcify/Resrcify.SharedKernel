using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

namespace Resrcify.SharedKernel.UnitOfWork.Interceptors;

/// <summary>Turns a delete into a soft delete, with <c>DeletedOnUtc</c> from <paramref name="timeProvider"/> (the system clock by default).</summary>
public sealed class UpdateDeletableEntitiesInterceptor(TimeProvider? timeProvider = null)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            UpdateDeletableEntities(
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
            UpdateDeletableEntities(
                eventData,
                eventData.Context);
        return await base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }
    private void UpdateDeletableEntities(
        DbContextEventData saving,
        DbContext context)
    {
        List<EntityEntry<IDeletableEntity>> entries = SaveChangesEntries.Where<IDeletableEntity>(
            saving,
            context,
            entry => entry.State == EntityState.Deleted);

        foreach (EntityEntry<IDeletableEntity> entityEntry in entries)
        {
            entityEntry.State = EntityState.Modified;
            entityEntry.Property(a => a.DeletedOnUtc)
                .CurrentValue = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            entityEntry.Property(a => a.IsDeleted)
                .CurrentValue = true;
        }
    }
}