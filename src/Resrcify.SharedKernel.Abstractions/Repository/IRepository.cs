using System.Threading;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq.Expressions;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

namespace Resrcify.SharedKernel.Abstractions.Repository;

public interface IRepository<TEntity, TId>
    where TEntity : class, IAggregateRoot<TId>
    where TId : notnull
{
    IAsyncEnumerable<TEntity> GetAllAsync();
    IAsyncEnumerable<TEntity> FindAsync(
        Expression<Func<TEntity, bool>> predicate);
    IAsyncEnumerable<TEntity> FindAsync(
        ISpecification<TEntity> specification);

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

    void Remove(
        TEntity entity);
    void RemoveRange(
        IEnumerable<TEntity> entities);
}