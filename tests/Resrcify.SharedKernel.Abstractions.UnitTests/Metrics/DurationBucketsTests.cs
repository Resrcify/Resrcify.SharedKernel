using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Resrcify.SharedKernel.Abstractions.Metrics;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Abstractions.UnitTests.Metrics;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class DurationBucketsTests
{
    [Fact]
    public void Seconds_ShouldRiseStrictly_FromFiveMillisecondsToAnHour()
    {
        var boundaries = DurationBuckets.Seconds;

        boundaries[0].ShouldBe(0.005);
        boundaries[^1].ShouldBe(3600);
        boundaries.Zip(boundaries.Skip(1)).ShouldAllBe(pair => pair.First < pair.Second);
    }

    [Fact]
    public void Seconds_ShouldTellApartTheDurationsOfARequestToAnotherService_BetweenATenthOfASecondAndTenSeconds()
    {
        var boundaries = DurationBuckets.Seconds;

        boundaries.Count(boundary => boundary is >= 0.1 and <= 10).ShouldBeGreaterThanOrEqualTo(8);
    }
}
