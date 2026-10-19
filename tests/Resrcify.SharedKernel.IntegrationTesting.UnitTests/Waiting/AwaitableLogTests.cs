using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Resrcify.SharedKernel.IntegrationTesting.Waiting;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.UnitTests.Waiting;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class AwaitableLogTests
{
    [Fact]
    public async Task WaitForAsync_ShouldCompleteAtOnce_WhenAMatchingEntryWasAlreadyAdded()
    {
        var log = new AwaitableLog<string>();
        log.Add("ping");
        log.Add("pong");

        var found = log.WaitForAsync(entry => entry.StartsWith('p') && entry.EndsWith('g'));

        found.IsCompletedSuccessfully.ShouldBeTrue();
        (await found).ShouldBe("ping");
    }

    [Fact]
    public async Task WaitForAsync_ShouldCompleteWithTheEntry_WhenAMatchingOneIsAdded()
    {
        var log = new AwaitableLog<string>();
        var found = log.WaitForAsync(entry => entry == "pong");

        log.Add("ping");
        found.IsCompleted.ShouldBeFalse();
        log.Add("pong");

        (await found).ShouldBe("pong");
    }

    [Fact]
    public async Task Add_ShouldCompleteEveryWaitItMatches()
    {
        var log = new AwaitableLog<int>();
        var even = log.WaitForAsync(entry => entry % 2 == 0);
        var large = log.WaitForAsync(entry => entry > 10);
        var negative = log.WaitForAsync(entry => entry < 0);

        log.Add(12);

        (await even).ShouldBe(12);
        (await large).ShouldBe(12);
        negative.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public void Entries_ShouldBeEverythingAdded_InOrder()
    {
        var log = new AwaitableLog<string>();
        log.Add("first");
        log.Add("second");

        log.Entries.ShouldBe(["first", "second"]);
    }
}
