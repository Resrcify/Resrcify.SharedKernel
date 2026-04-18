using System.Threading;
using System;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

namespace Resrcify.SharedKernel.Abstractions.Repository;

public interface INullableFetchRepository<TEntity, TId>
    where TEntity : class, IAggregateRoot<TId>
    where TId : notnull
{
    Task<TEntity?> GetByIdAsync(
        TId id,
        CancellationToken cancellationToken = default);

    Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<TEntity?> FirstOrDefaultAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default);
}