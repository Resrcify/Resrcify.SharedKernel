using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Results.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ResultFunctionalPatternTests
{
    [Fact]
    public void Chain_CombineMapTapBindEnsureMatch_WithSuccess_ShouldProduceExpectedValue()
    {
        var sideEffect = 0;

        var value = Result.Combine(
                Result.Success(2),
                Result.Success(3),
                Result.Success(5))
            .Map(tuple => tuple.Item1 + tuple.Item2 + tuple.Item3)
            .Tap(sum => sideEffect = sum)
            .Bind(sum => Result.Success($"sum:{sum}"))
            .Ensure(text => text == "sum:10", Error.Conflict("Result.InvalidSum", "Unexpected sum."))
            .Match(
                onSuccess: text => text,
                onFailure: _ => "failed");

        sideEffect.ShouldBe(10);
        value.ShouldBe("sum:10");
    }

    [Fact]
    public void Chain_WithFailure_ShouldShortCircuitMapTapAndBind()
    {
        var mapCalled = false;
        var tapCalled = false;
        var bindCalled = false;
        var error = Error.Validation("Result.Invalid", "Invalid input.");

        var result = Result.Combine(
                Result.Success(10),
                Result.Failure<int>(error),
                Result.Failure<int>(error))
            .Map(tuple =>
            {
                mapCalled = true;
                return tuple.Item1 + tuple.Item2 + tuple.Item3;
            })
            .Tap(_ => tapCalled = true)
            .Bind(value =>
            {
                bindCalled = true;
                return Result.Success(value * 2);
            });

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldContain(error);
        result.Errors.Length.ShouldBe(1);
        mapCalled.ShouldBeFalse();
        tapCalled.ShouldBeFalse();
        bindCalled.ShouldBeFalse();
    }

    [Fact]
    public void Combine_NonGeneric_ShouldAggregateDistinctErrors()
    {
        var first = Error.Validation("Result.Name.Empty", "Name is required.");
        var second = Error.Conflict("Result.Name.Duplicate", "Name already exists.");

        var result = Result.Combine(
            Result.Failure([first, second]),
            Result.Failure([first]),
            Result.Success());

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBeEquivalentTo(new[] { first, second });
        result.Errors.Length.ShouldBe(2);
    }

    [Fact]
    public void Combine_Arity15_WithSuccess_ShouldReturnTupleThatCanBeMapped()
    {
        var combined = Result.Combine(
            Result.Success(1),
            Result.Success(2),
            Result.Success(3),
            Result.Success(4),
            Result.Success(5),
            Result.Success(6),
            Result.Success(7),
            Result.Success(8),
            Result.Success(9),
            Result.Success(10),
            Result.Success(11),
            Result.Success(12),
            Result.Success(13),
            Result.Success(14),
            Result.Success(15));

        var mapped = combined.Map(tuple => tuple.Item1 + tuple.Item15);

        combined.IsSuccess.ShouldBeTrue();
        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(16);
    }

    [Fact]
    public void Combine_Arity15_WithFailures_ShouldAggregateDistinctErrors()
    {
        var first = Error.Validation("Result.Rule.1", "Rule 1 failed.");
        var second = Error.Validation("Result.Rule.2", "Rule 2 failed.");

        var combined = Result.Combine(
            Result.Success(1),
            Result.Success(2),
            Result.Success(3),
            Result.Success(4),
            Result.Failure<int>(first),
            Result.Success(6),
            Result.Success(7),
            Result.Success(8),
            Result.Success(9),
            Result.Success(10),
            Result.Success(11),
            Result.Failure<int>(second),
            Result.Success(13),
            Result.Failure<int>(first),
            Result.Success(15));

        combined.IsFailure.ShouldBeTrue();
        combined.Errors.ShouldBeEquivalentTo(new[] { first, second });
        combined.Errors.Length.ShouldBe(2);
    }

    [Fact]
    public void Match_WithOptionalError_ShouldUseProvidedErrorWhenSourceFails()
    {
        var source = Result.Failure<int>(Error.NotFound("Result.Source", "Source failed."));
        var mappedError = Error.ExternalFailure("Result.Mapped", "Mapped failure.");

        var mapped = source.Match(value => value * 2, mappedError);

        mapped.IsFailure.ShouldBeTrue();
        mapped.Errors.ShouldBe([mappedError]);
    }

    [Fact]
    public void Match_WithOptionalError_ShouldMapValueWhenSourceSucceeds()
    {
        var source = Result.Success(21);

        var mapped = source.Match(value => value * 2, Error.NullValue);

        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Tap_WithAsyncResultFunc_ShouldTurnIntoFailureWhenTapFails()
    {
        var tapError = Error.Timeout("Result.Tap.Timeout", "Tap operation timed out.");

        var result = await Task.FromResult(Result.Success(123))
            .Tap(_ => Task.FromResult(Result.Failure(tapError)));

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([tapError]);
    }

    [Fact]
    public async Task AsyncChain_EnsureMapBindTapMatch_ShouldComposeCorrectly()
    {
        var tappedValue = 0;

        var text = await Task.FromResult(Result.Success(3))
            .Ensure(async value =>
            {
                await Task.Yield();
                return value > 0;
            }, Error.Validation("Result.NonPositive", "Value must be positive."))
            .Map(async value =>
            {
                await Task.Yield();
                return value * 4;
            })
            .Bind(async value =>
            {
                await Task.Yield();
                return Result.Success($"value:{value + 1}");
            })
            .Tap(async value =>
            {
                await Task.Yield();
                tappedValue = value.Length;
            })
            .Match(
                onSuccess: value => value,
                onFailure: errors => errors[0].Code);

        text.ShouldBe("value:13");
        tappedValue.ShouldBe("value:13".Length);
    }
}
