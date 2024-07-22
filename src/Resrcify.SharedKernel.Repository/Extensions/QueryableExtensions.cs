using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Resrcify.SharedKernel.Repository.Extensions;

public static class QueryableExtensions
{
    public static IQueryable<T> WhereIf<T>(
        this IQueryable<T> queryable,
        bool condition,
        Expression<Func<T, bool>> predicate)
        => condition
            ? queryable.Where(predicate)
            : queryable;

    public static IQueryable<T> IncludeIf<T>(
        this IQueryable<T> queryable,
        bool condition,
        Expression<Func<T, object>> keySelector)
        where T : class
        => condition
            ? queryable.Include(keySelector)
            : queryable;

}