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
    public void IsTransient_ShouldBeFalse_WhenAnotherTryWouldFailTheSameWay(ErrorType type)
        => type.IsTransient().ShouldBeFalse();

    [Theory]
    [InlineData(ErrorType.Failure)]
    [InlineData(ErrorType.ExternalFailure)]
    [InlineData(ErrorType.Timeout)]
    [InlineData(ErrorType.RateLimit)]
    public void IsTransient_ShouldBeTrue_WhenAnotherTryMayPass(ErrorType type)
        => type.IsTransient().ShouldBeTrue();
}
