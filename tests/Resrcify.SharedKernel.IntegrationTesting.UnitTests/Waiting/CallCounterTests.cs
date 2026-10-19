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
public sealed class CallCounterTests
{
    [Fact]
    public void ReachedAsync_ShouldBeCompleted_WhenTheCallsWereAlreadyMade()
    {
        var calls = new CallCounter();
        calls.Increment();
        calls.Increment();

        calls.ReachedAsync(2).IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task ReachedAsync_ShouldComplete_WhenTheCountIsReached()
    {
        var calls = new CallCounter();
        var third = calls.ReachedAsync(3);

        calls.Increment();
        calls.Increment();
        third.IsCompleted.ShouldBeFalse();
        calls.Increment();

        await third;
        calls.Count.ShouldBe(3);
    }

    [Fact]
    public void Increment_ShouldCompleteOnlyTheWaitsItReached()
    {
        var calls = new CallCounter();
        var first = calls.ReachedAsync(1);
        var second = calls.ReachedAsync(2);

        calls.Increment();

        first.IsCompletedSuccessfully.ShouldBeTrue();
        second.IsCompleted.ShouldBeFalse();
    }
}
