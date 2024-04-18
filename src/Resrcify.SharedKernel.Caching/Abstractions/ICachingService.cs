using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.Caching.Abstractions;

public interface ICachingService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class;
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task<IEnumerable<T?>> GetBulkAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default);
}