using System.Collections.Generic;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>What came back for a scatter: results, failures, and the items that never answered.</summary>
public interface IGathered<TResponse>
    where TResponse : class
{
    /// <summary>The responses, by item key.</summary>
    IReadOnlyDictionary<string, TResponse> Results { get; }

    /// <summary>Items whose responder answered with a failure, with its errors (e.g. "not found").</summary>
    IReadOnlyDictionary<string, IReadOnlyList<Error>> Failures { get; }

    /// <summary>Items that never answered before the timeout.</summary>
    IReadOnlyList<string> UnansweredKeys { get; }

    /// <summary>
    /// Items with a definite answer: a result, or a failure that is the request's fault (no error
    /// <see cref="ErrorTypeExtensions.IsTransient"/>, e.g. "not found"). Not the items that never answered, nor
    /// those whose responder kept failing until its last try (e.g. its upstream was down): an all-or-nothing gather
    /// applies only when every item it asked for is here.
    /// </summary>
    IReadOnlyCollection<string> SettledKeys { get; }

    /// <summary>Every item answered, with a result or a failure.</summary>
    bool IsComplete { get; }

    /// <summary>
    /// One item as a result: its response, its responder's errors, or, if it never answered, an error with code
    /// <c>ScatterGather.Unanswered</c> (<see cref="ErrorType.Timeout"/>).
    /// </summary>
    Result<TResponse> this[string key] { get; }
}
