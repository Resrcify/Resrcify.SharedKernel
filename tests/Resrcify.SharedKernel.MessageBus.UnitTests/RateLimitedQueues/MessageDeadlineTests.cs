using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Rebus.Messages;
using Resrcify.SharedKernel.MessageBus.RateLimitedQueues;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.RateLimitedQueues;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageDeadlineTests
{
    [Fact]
    public void TryRead_ShouldGiveSentTimePlusTimeToLive_WhenBothHeadersAreThere()
    {
        var sent = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var headers = new Dictionary<string, string>
        {
            [Headers.SentTime] = sent.ToString("O"),
            [Headers.TimeToBeReceived] = "00:00:50",
        };

        MessageDeadline.TryRead(headers, out var deadline).ShouldBeTrue();
        deadline.ShouldBe(sent.AddSeconds(50));
    }

    [Theory]
    [InlineData(-26 * 365 * 24.0, 50)]   // this clock years behind the sender's (a test's FakeTimeProvider)
    [InlineData(0.0, 50)]
    public void Remaining_ShouldNeverExceedTheTimeToLive_WhenTheClocksDisagree(double hoursFromSent, int expectedSeconds)
    {
        var sent = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var headers = new Dictionary<string, string>
        {
            [Headers.SentTime] = sent.ToString("O"),
            [Headers.TimeToBeReceived] = "00:00:50",
        };

        MessageDeadline.Remaining(headers, sent.AddHours(hoursFromSent)).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void Remaining_ShouldBeWhatIsLeft_WhenTheRequestWaited()
    {
        var sent = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var headers = new Dictionary<string, string>
        {
            [Headers.SentTime] = sent.ToString("O"),
            [Headers.TimeToBeReceived] = "00:00:50",
        };

        MessageDeadline.Remaining(headers, sent.AddSeconds(20)).ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TryRead_ShouldReturnFalse_WhenTheMessageHasNoTimeToLive()
        => MessageDeadline.TryRead(
            new Dictionary<string, string> { [Headers.SentTime] = DateTimeOffset.UtcNow.ToString("O") },
            out _).ShouldBeFalse();

    [Fact]
    public void TryRead_ShouldReturnFalse_WhenAHeaderIsUnreadable()
        => MessageDeadline.TryRead(
            new Dictionary<string, string>
            {
                [Headers.SentTime] = "yesterday",
                [Headers.TimeToBeReceived] = "00:00:50",
            },
            out _).ShouldBeFalse();
}
