using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Configuration;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageFailuresTests
{
    [Fact]
    public void AreTheMessagesFault_ShouldBeTrue_WhenEveryErrorIsAboutTheMessage()
        => MessageFailures.AreTheMessagesFault(
            [Error.NotFound("Player.NotFound", "No such player."), Error.Conflict("Player.Taken", "Taken.")])
            .ShouldBeTrue();

    [Fact]
    public void AreTheMessagesFault_ShouldBeFalse_ForAConcurrencyConflict_WhichATryInANewScopeReadsAgain()
        => MessageFailures.AreTheMessagesFault(
            [Error.Conflict(ErrorTypeExtensions.ConcurrencyConflictCode, "Changed since it was read.")])
            .ShouldBeFalse();

    [Fact]
    public void AreTheMessagesFault_ShouldBeFalse_WhenAnyErrorIsTransient()
        => MessageFailures.AreTheMessagesFault(
            [Error.NotFound("Player.NotFound", "No such player."), Error.ExternalFailure("Game.Down", "Down.")])
            .ShouldBeFalse();

    [Fact]
    public void AreTheMessagesFault_ShouldBeFalse_WithoutErrors()
        => MessageFailures.AreTheMessagesFault([]).ShouldBeFalse();
}
