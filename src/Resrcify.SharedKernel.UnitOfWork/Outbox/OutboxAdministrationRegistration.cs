using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Outbox;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

internal static class OutboxAdministrationRegistration
{
    /// <summary>
    /// <typeparamref name="TDbContext"/>'s outbox administration, scoped, as <see cref="OutboxAdministration{TDbContext}"/>
    /// and as <see cref="IOutboxAdministration{TContext}"/> (the same instance in a scope), unless registered already.
    /// </summary>
    public static IServiceCollection TryAddOutboxAdministration<TDbContext>(
        this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.TryAddScoped<OutboxAdministration<TDbContext>>();
        services.TryAddScoped<IOutboxAdministration<TDbContext>>(
            provider => provider.GetRequiredService<OutboxAdministration<TDbContext>>());
        return services;
    }
}
