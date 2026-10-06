using System;
using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Results.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ResultFactoryTests
{
    private static readonly Error First = Error.Validation("Name.Empty", "The name is empty.");
    private static readonly Error Second = Error.NotFound("Shard.NotFound", "No such shard.");

    [Fact]
    public void Failure_ShouldMakeAFailedResult_WhenTheTypeIsResult()
    {
        var result = ResultFactory.Failure<Result>([First, Second]);

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([First, Second]);
    }

    [Fact]
    public void Failure_ShouldMakeAFailedResultOfTheValueType_WhenTheTypeIsAResultOfAValue()
    {
        var result = ResultFactory.Failure<Result<int>>([First, Second]);

        result.ShouldBeOfType<Result<int>>();
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([First, Second]);
    }

    [Fact]
    public void Failure_ShouldMakeAFailedResultWithTheError_WhenGivenOneError()
    {
        var result = ResultFactory.Failure<Result<string>>(First);

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([First]);
    }

    [Fact]
    public void Failure_ShouldMakeANewResultEachTime_WhenCalledAgainForTheSameType()
    {
        var first = ResultFactory.Failure<Result<int>>(First);
        var second = ResultFactory.Failure<Result<int>>(Second);

        first.Errors.ShouldBe([First]);
        second.Errors.ShouldBe([Second]);
    }

    [Fact]
    public void Failure_ShouldThrow_WhenThereAreNoErrors()
        => Should.Throw<ArgumentException>(() => ResultFactory.Failure<Result<int>>([]));

    [Fact]
    public void Failure_ShouldThrowNotSupported_WhenTheTypeDerivesFromAResult()
        => Should.Throw<NotSupportedException>(() => ResultFactory.Failure<DerivedResult>(First));

    private sealed class DerivedResult()
        : Result(false, First);
}
