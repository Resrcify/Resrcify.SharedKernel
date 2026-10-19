using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resrcify.SharedKernel.Abstractions.MessageBus;

/// <summary>
/// What came of a scatter, counted (<see cref="Of"/>): the items asked for, those answered with a result, the settled
/// failures by their first error's code (the request's fault, e.g. <c>RECORDNOTFOUND</c>), the failures the responder
/// gave up on (it kept failing until its last try, e.g. its upstream was down) and the items never answered. For a
/// summary line; a count that needs the responses themselves (an answer that is unusable) stays the caller's.
/// </summary>
/// <param name="Asked">The items asked for: answered, failed or unanswered.</param>
/// <param name="Answered">The items answered with a result.</param>
/// <param name="FailedByCode">The settled failures, by their first error's code (<see cref="NoErrorCode"/> without one).</param>
/// <param name="GaveUp">The failures that aren't settled (not in <see cref="IGathered{TResponse}.SettledKeys"/>).</param>
/// <param name="Unanswered">The items with no answer before the timeout.</param>
public sealed record GatheredCounts(
    int Asked,
    int Answered,
    IReadOnlyDictionary<string, int> FailedByCode,
    int GaveUp,
    int Unanswered)
{
    /// <summary>The code a settled failure is counted under when its answer carried no error.</summary>
    public const string NoErrorCode = "Unknown";

    /// <summary>Nothing asked: the start of a sum over several scatters (<see cref="Add"/>).</summary>
    public static GatheredCounts None { get; } = new(0, 0, new Dictionary<string, int>(StringComparer.Ordinal), 0, 0);

    /// <summary>The settled failures in all.</summary>
    public int Failed
        => FailedByCode.Values.Sum();

    /// <summary>
    /// The settled failures by code, in ordinal order, in parentheses after a space
    /// (<c> (RECORDNOTFOUND 3, Unknown 1)</c>), or nothing when none failed, so a line reads <c>failed 0</c> or
    /// <c>failed 4 (RECORDNOTFOUND 3, Unknown 1)</c>. Write it literal in a Serilog template (<c>{FailedCodes:l}</c>):
    /// Serilog would otherwise quote the string.
    /// </summary>
    public string FailedCodes
        => FailedByCode.Count == 0
            ? string.Empty
            : $" ({string.Join(", ", FailedByCode.OrderBy(failure => failure.Key, StringComparer.Ordinal).Select(Render))})";

    /// <summary>What came of <paramref name="gathered"/>.</summary>
    public static GatheredCounts Of<TResponse>(IGathered<TResponse> gathered)
        where TResponse : class
    {
        ArgumentNullException.ThrowIfNull(gathered);

        var failedByCode = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var gaveUp = 0;
        foreach (var (key, errors) in gathered.Failures)
        {
            if (!gathered.SettledKeys.Contains(key))
            {
                gaveUp++;
                continue;
            }

            var code = errors.Count > 0 ? errors[0].Code : NoErrorCode;
            failedByCode[code] = failedByCode.GetValueOrDefault(code) + 1;
        }

        return new GatheredCounts(
            gathered.Results.Count + gathered.Failures.Count + gathered.UnansweredKeys.Count,
            gathered.Results.Count,
            failedByCode,
            gaveUp,
            gathered.UnansweredKeys.Count);
    }

    /// <summary>These counts and <paramref name="other"/>'s together (several scatters, e.g. one per chunk).</summary>
    public GatheredCounts Add(GatheredCounts other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var failedByCode = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (code, count) in FailedByCode.Concat(other.FailedByCode))
            failedByCode[code] = failedByCode.GetValueOrDefault(code) + count;

        return new GatheredCounts(
            Asked + other.Asked,
            Answered + other.Answered,
            failedByCode,
            GaveUp + other.GaveUp,
            Unanswered + other.Unanswered);
    }

    /// <inheritdoc cref="Add"/>
    public static GatheredCounts operator +(
        GatheredCounts left,
        GatheredCounts right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.Add(right);
    }

    private static string Render(KeyValuePair<string, int> failure)
        => string.Create(CultureInfo.InvariantCulture, $"{failure.Key} {failure.Value}");
}
