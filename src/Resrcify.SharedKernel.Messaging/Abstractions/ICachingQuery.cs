using System;

namespace Resrcify.SharedKernel.Messaging.Abstractions;

public interface ICachingQuery<TResponse>
    : IQuery<TResponse>, ICachingQuery;

public interface ICachingQuery
{
    string? CacheKey { get; set; }
    TimeSpan Expiration { get; set; }
}