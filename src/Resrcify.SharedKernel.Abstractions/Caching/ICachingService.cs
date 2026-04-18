using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Resrcify.SharedKernel.Abstractions.Caching;

public interface ICachingService
{
    Task<T?> GetAsync<T>(
        string key,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<T?> GetAsync<T>(
        string key,
        CancellationToken cancellationToken = default)
        where T : class
        => GetAsync<T>(
            key,
            serializerOptions: null,
            cancellationToken: cancellationToken);

    Task SetAsync<T>(
       string key,
       T value,
       TimeSpan? slidingExpiration = null,
       DateTimeOffset? absoluteExpiration = null,
       JsonSerializerOptions? serializerOptions = null,
       CancellationToken cancellationToken = default)
       where T : class;

    Task SetAsync<T>(
        string key,
        T value,
        CancellationToken cancellationToken = default)
        where T : class
        => SetAsync(
            key,
            value,
            slidingExpiration: null,
            absoluteExpiration: null,
            serializerOptions: null,
            cancellationToken: cancellationToken);

    Task SetAsync<T>(
        string key,
        T value,
        JsonSerializerOptions serializerOptions,
        CancellationToken cancellationToken = default)
        where T : class
        => SetAsync(
            key,
            value,
            slidingExpiration: null,
            absoluteExpiration: null,
            serializerOptions: serializerOptions,
            cancellationToken: cancellationToken);

    Task SetAsync<T>(
        string key,
        T value,
        DateTimeOffset absoluteExpiration,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        where T : class
        => SetAsync(
            key,
            value,
            slidingExpiration: null,
            absoluteExpiration: absoluteExpiration,
            serializerOptions: serializerOptions,
            cancellationToken: cancellationToken);

    Task SetAsync<T>(
        string key,
        T value,
        DateTimeOffset absoluteExpiration,
        CancellationToken cancellationToken)
        where T : class
        => SetAsync(
            key,
            value,
            absoluteExpiration,
            serializerOptions: null,
            cancellationToken: cancellationToken);

    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan slidingExpiration,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
        where T : class
        => SetAsync(
            key,
            value,
            slidingExpiration: slidingExpiration,
            absoluteExpiration: null,
            serializerOptions: serializerOptions,
            cancellationToken: cancellationToken);

    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan slidingExpiration,
        CancellationToken cancellationToken)
        where T : class
        => SetAsync(
            key,
            value,
            slidingExpiration,
            serializerOptions: null,
            cancellationToken: cancellationToken);

    Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<T?>> GetBulkAsync<T>(
        IEnumerable<string> keys,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<T?>> GetBulkAsync<T>(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
        => GetBulkAsync<T>(
            keys,
            serializerOptions: null,
            cancellationToken: cancellationToken);
}