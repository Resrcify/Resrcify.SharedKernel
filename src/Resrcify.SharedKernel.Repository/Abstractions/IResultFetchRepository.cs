using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.Repository.Primitives;

namespace Resrcify.SharedKernel.Repository.Abstractions;

public interface IResultFetchRepository<TEntity, TId>
    where TEntity : AggregateRoot<TId>
    where TId : notnull
{
    Task<Result<TEntity>> GetByIdAsync(
        TId id,
        CancellationToken cancellationToken = default);

    Task<Result<TEntity>> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<Result<TEntity>> FirstOrDefaultAsync(
        Specification<TEntity, TId> specification,
        CancellationToken cancellationToken = default);
}