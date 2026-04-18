using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.Repository;

public interface IResultFetchRepository<TEntity, TId>
    where TEntity : class, IAggregateRoot<TId>
    where TId : notnull
{
    Task<Result<TEntity>> GetByIdAsync(
        TId id,
        CancellationToken cancellationToken = default);

    Task<Result<TEntity>> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<Result<TEntity>> FirstOrDefaultAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default);
}
