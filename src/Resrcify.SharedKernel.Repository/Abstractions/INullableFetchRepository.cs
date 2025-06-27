using System.Threading;
using System;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Resrcify.SharedKernel.Repository.Primitives;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.Repository.Abstractions;

public interface INullableFetchRepository<TEntity, TId>
    where TEntity : AggregateRoot<TId>
    where TId : notnull
{
    Task<TEntity?> GetByIdAsync(
        TId id,
        CancellationToken cancellationToken = default);

    Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<TEntity?> FirstOrDefaultAsync(
        Specification<TEntity, TId> specification,
        CancellationToken cancellationToken = default);
}