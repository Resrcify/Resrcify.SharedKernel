using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Results.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ErrorTypeExtensionsTests
{
    [Theory]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.Unprocessable)]
    public void IsTransient_ShouldBeFalse_WhenAnotherTryWouldFailTheSameWay(ErrorType type)
        => type.IsTransient().ShouldBeFalse();

    [Theory]
    [InlineData(ErrorType.Failure)]
    [InlineData(ErrorType.ExternalFailure)]
    [InlineData(ErrorType.Timeout)]
    [InlineData(ErrorType.RateLimit)]
    public void IsTransient_ShouldBeTrue_WhenAnotherTryMayPass(ErrorType type)
        => type.IsTransient().ShouldBeTrue();

    [Fact]
    public void IsTransientError_ShouldBeTrue_ForAConcurrencyConflict_ThatAnotherTryReadsAgain()
        => Error.Conflict(ErrorTypeExtensions.ConcurrencyConflictCode, "Changed since it was read.")
            .IsTransient()
            .ShouldBeTrue();

    [Fact]
    public void IsTransientError_ShouldBeFalse_ForAnyOtherConflict()
        => Error.Conflict("Player.Taken", "The name is taken.").IsTransient().ShouldBeFalse();

    [Theory]
    [InlineData(ErrorType.Failure, true)]
    [InlineData(ErrorType.NotFound, false)]
    public void IsTransientError_ShouldFollowTheType_Otherwise(ErrorType type, bool transient)
        => new Error("Some.Code", "Some message.", type).IsTransient().ShouldBe(transient);
}
