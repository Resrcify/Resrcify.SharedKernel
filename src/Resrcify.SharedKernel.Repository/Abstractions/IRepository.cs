using System.Threading;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.Repository.Primitives;

namespace Resrcify.SharedKernel.Repository.Abstractions;

public interface IRepository<TEntity, TId>
    where TEntity : AggregateRoot<TId>
    where TId : notnull
{
    IAsyncEnumerable<TEntity> GetAllAsync();
    IAsyncEnumerable<TEntity> FindAsync(Expression<Func<TEntity, bool>> predicate);
    IAsyncEnumerable<TEntity> FindAsync(Specification<TEntity, TId> specification);

    Task<bool> ExistsAsync(
        TId id,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default);

    void Remove(TEntity entity);
    void RemoveRange(IEnumerable<TEntity> entities);
}