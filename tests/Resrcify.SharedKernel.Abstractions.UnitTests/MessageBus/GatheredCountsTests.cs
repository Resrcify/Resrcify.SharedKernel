using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Abstractions.UnitTests.MessageBus;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class GatheredCountsTests
{
    private static readonly Error NotFound = Error.NotFound("RECORDNOTFOUND", "No such player.");
    private static readonly Error Down = Error.ExternalFailure("Game.Down", "The game is down.");

    [Fact]
    public void Of_ShouldCountEveryOutcome()
    {
        var gathered = new FakeGathered(
            results: ["a", "b"],
            failures: new Dictionary<string, IReadOnlyList<Error>>(StringComparer.Ordinal)
            {
                ["c"] = [NotFound],
                ["d"] = [NotFound, Down],
                ["e"] = [],
                ["f"] = [Down],
            },
            unanswered: ["g"],
            unsettled: ["f"]);

        var counts = GatheredCounts.Of(gathered);

        counts.Asked.ShouldBe(7);
        counts.Answered.ShouldBe(2);
        counts.FailedByCode.ShouldBe(
            new Dictionary<string, int> { ["RECORDNOTFOUND"] = 2, [GatheredCounts.NoErrorCode] = 1 },
            ignoreOrder: true);
        counts.Failed.ShouldBe(3);
        counts.GaveUp.ShouldBe(1);
        counts.Unanswered.ShouldBe(1);
    }

    [Fact]
    public void FailedCodes_ShouldListTheCodesInOrdinalOrder_WithTheirCounts()
    {
        var counts = new GatheredCounts(
            9,
            4,
            new Dictionary<string, int> { ["Unknown"] = 1, ["RECORDNOTFOUND"] = 3 },
            1,
            0);

        counts.FailedCodes.ShouldBe(" (RECORDNOTFOUND 3, Unknown 1)");
    }

    [Fact]
    public void FailedCodes_ShouldBeEmpty_WhenNothingFailed()
        => GatheredCounts
            .Of(new FakeGathered(["a"], new Dictionary<string, IReadOnlyList<Error>>(StringComparer.Ordinal), [], []))
            .FailedCodes.ShouldBeEmpty();

    [Fact]
    public void Add_ShouldSumTheCounts_AndTheFailuresByCode()
    {
        var first = new GatheredCounts(5, 3, new Dictionary<string, int> { ["RECORDNOTFOUND"] = 1 }, 1, 0);
        var second = new GatheredCounts(4, 1, new Dictionary<string, int> { ["RECORDNOTFOUND"] = 2, ["Unknown"] = 1 }, 0, 1);

        var total = GatheredCounts.None + first + second;

        total.Asked.ShouldBe(9);
        total.Answered.ShouldBe(4);
        total.FailedByCode.ShouldBe(new Dictionary<string, int> { ["RECORDNOTFOUND"] = 3, ["Unknown"] = 1 }, ignoreOrder: true);
        total.GaveUp.ShouldBe(1);
        total.Unanswered.ShouldBe(1);
    }

    private sealed class FakeGathered(
        IReadOnlyCollection<string> results,
        IReadOnlyDictionary<string, IReadOnlyList<Error>> failures,
        IReadOnlyList<string> unanswered,
        IReadOnlyCollection<string> unsettled)
        : IGathered<string>
    {
        public IReadOnlyDictionary<string, string> Results { get; }
            = results.ToDictionary(key => key, key => key, StringComparer.Ordinal);

        public IReadOnlyDictionary<string, IReadOnlyList<Error>> Failures
            => failures;

        public IReadOnlyList<string> UnansweredKeys
            => unanswered;

        public IReadOnlyCollection<string> SettledKeys
            => [.. results.Concat(failures.Keys).Except(unsettled, StringComparer.Ordinal)];

        public bool IsComplete
            => unanswered.Count == 0;

        public Result<string> this[string key]
            => Results.TryGetValue(key, out var value) ? value : Result.Failure<string>(NotFound);
    }
}
