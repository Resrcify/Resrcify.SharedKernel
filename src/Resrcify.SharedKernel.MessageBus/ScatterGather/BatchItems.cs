using System.Collections.Generic;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.ScatterGather;

/// <summary>How a batch's items ended, for its metrics.</summary>
internal readonly record struct BatchItems(int Answered, int Failed, int GaveUp, int Unanswered)
{
    /// <summary>Counts one more reply: a response, a failure that is the request's fault, or one its responder gave up on.</summary>
    public BatchItems Add(Result result)
    {
        if (result.IsSuccess)
            return this with { Answered = Answered + 1 };
        return IsTheRequestsFault(result.Errors)
            ? this with { Failed = Failed + 1 }
            : this with { GaveUp = GaveUp + 1 };
    }

    public int Replied => Answered + Failed + GaveUp;

    public static BatchItems Of<TResponse>(Gathered<TResponse> gathered)
        where TResponse : class
        => new(
            gathered.Results.Count,
            gathered.SettledKeys.Count - gathered.Results.Count,
            gathered.Failures.Count + gathered.Results.Count - gathered.SettledKeys.Count,
            gathered.UnansweredKeys.Count);

    private static bool IsTheRequestsFault(IReadOnlyList<Error> errors)
        => MessageFailures.AreTheMessagesFault(errors);
}
