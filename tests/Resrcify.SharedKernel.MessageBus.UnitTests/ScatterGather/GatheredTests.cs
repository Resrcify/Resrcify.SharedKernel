using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.ScatterGather;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class GatheredTests
{
    [Fact]
    public void SettledKeys_ShouldHoldTheResultsAndTheDefiniteFailures_WhenSomeItemsGaveUpOrNeverAnswered()
    {
        var gathered = new Gathered<string>(
            ["found", "unknown", "upstream-down", "silent"],
            new Dictionary<string, string> { ["found"] = "profile" },
            new Dictionary<string, IReadOnlyList<Error>>
            {
                ["unknown"] = [new Error("Player.NotFound", "No such player", ErrorType.NotFound)],
                ["upstream-down"] = [new Error("Game.Down", "The game is down", ErrorType.ExternalFailure)],
            });

        gathered.SettledKeys.ShouldBe(["found", "unknown"], ignoreOrder: true);
        gathered.UnansweredKeys.ShouldBe(["silent"]);
    }

    [Fact]
    public void SettledKeys_ShouldLeaveOutAFailure_WhenOnlySomeOfItsErrorsAreTheCallersFault()
    {
        var gathered = new Gathered<string>(
            ["mixed"],
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<Error>>
            {
                ["mixed"] =
                [
                    new Error("Player.NotFound", "No such player", ErrorType.NotFound),
                    new Error("Game.Timeout", "The game timed out", ErrorType.Timeout),
                ],
            });

        gathered.SettledKeys.ShouldBeEmpty();
    }
}
