using System;

namespace Resrcify.SharedKernel.Messaging.Abstractions;

public interface ICachingQuery<TResponse>
    : IQuery<TResponse>, ICacheable;

public interface ICacheable
{
    string? CacheKey { get; set; }
    TimeSpan Expiration { get; set; }
}