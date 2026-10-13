using System;
using System.Collections.Generic;
using System.Linq;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <inheritdoc cref="IGathered{TResponse}"/>
internal sealed class Gathered<TResponse>
    : IGathered<TResponse>
    where TResponse : class
{
    public Gathered(
        IReadOnlyCollection<string> requestedKeys,
        IReadOnlyDictionary<string, TResponse> results,
        IReadOnlyDictionary<string, IReadOnlyList<Error>> failures)
    {
        ArgumentNullException.ThrowIfNull(requestedKeys);
        Results = results ?? throw new ArgumentNullException(nameof(results));
        Failures = failures ?? throw new ArgumentNullException(nameof(failures));
        UnansweredKeys = requestedKeys
            .Where(key => !results.ContainsKey(key) && !failures.ContainsKey(key))
            .ToList();
        SettledKeys = results.Keys
            .Concat(failures.Where(failure => MessageFailures.AreTheMessagesFault(failure.Value)).Select(failure => failure.Key))
            .ToHashSet(StringComparer.Ordinal);
    }

    public static Gathered<TResponse> Empty { get; } = new([], new Dictionary<string, TResponse>(), new Dictionary<string, IReadOnlyList<Error>>());

    public IReadOnlyDictionary<string, TResponse> Results { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<Error>> Failures { get; }

    public IReadOnlyList<string> UnansweredKeys { get; }

    public IReadOnlyCollection<string> SettledKeys { get; }

    public bool IsComplete => UnansweredKeys.Count == 0;

    public Result<TResponse> this[string key]
    {
        get
        {
            if (Results.TryGetValue(key, out var response))
                return response;
            if (Failures.TryGetValue(key, out var errors))
                return Result.Failure<TResponse>(errors);
            return ScatterGatherErrors.Unanswered(key);
        }
    }
}
