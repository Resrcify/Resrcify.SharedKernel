using Microsoft.EntityFrameworkCore;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Outbox;

/// <summary>
/// The outbox interceptor of <typeparamref name="TContext"/>, with that context's insert strategy: one per context, so
/// two contexts set up with different outbox options each write their outbox their own way.
/// </summary>
internal sealed record PostgresOutboxInterceptor<TContext>(InsertOutboxMessagesInterceptor Interceptor)
    where TContext : DbContext
{
    /// <summary>The DbContext this interceptor belongs to (the type parameter keys it in DI).</summary>
    public static System.Type DbContextType => typeof(TContext);
}
