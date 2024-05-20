using System;
using System.Linq;
using System.Linq.Expressions;

namespace Resrcify.SharedKernel.Repository.Extensions;

public static class QueryableExtensions
{
    public static IQueryable<T> WhereIf<T>(this IQueryable<T> queryable, bool condition, Expression<Func<T, bool>> predicate)
        => condition ? queryable.Where(predicate) : queryable;
}