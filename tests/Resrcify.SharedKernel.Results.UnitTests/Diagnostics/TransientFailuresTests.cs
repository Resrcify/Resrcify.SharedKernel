using System;
using System.Collections.Immutable;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NSubstitute;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Results.UnitTests.Diagnostics;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class TransientFailuresTests
{
    private static readonly Error Unreachable = Error.ExternalFailure("SwgohApi.Unreachable", "SwgohApi could not be reached.");
    private static readonly Error NotFound = Error.NotFound("Player.NotFound", "No such player.");

    public static TheoryData<string, Exception, bool> Failures => new()
    {
        { "a transient failure", new TransientFailureException([Unreachable]), true },
        { "a transient failure without errors", new TransientFailureException("down"), true },
        { "HTTP 503", Http(HttpStatusCode.ServiceUnavailable), true },
        { "HTTP 500", Http(HttpStatusCode.InternalServerError), true },
        { "HTTP 502", Http(HttpStatusCode.BadGateway), true },
        { "HTTP 504", Http(HttpStatusCode.GatewayTimeout), true },
        { "HTTP 408", Http(HttpStatusCode.RequestTimeout), true },
        { "HTTP 429", Http(HttpStatusCode.TooManyRequests), true },
        { "HTTP 400", Http(HttpStatusCode.BadRequest), false },
        { "HTTP 404", Http(HttpStatusCode.NotFound), false },
        { "HTTP 409", Http(HttpStatusCode.Conflict), false },
        { "an HTTP request that got no answer", new HttpRequestException("no answer"), true },
        { "a timeout", new TimeoutException("too slow"), true },
        { "a handler's own cancellation", new OperationCanceledException("cancelled"), true },
        { "a task cancellation (an HttpClient timeout)", new TaskCanceledException("timed out"), true },
        { "a transient database failure", Database(transient: true), true },
        { "a database failure", Database(transient: false), false },
        { "a handler's bug", new InvalidOperationException("broken"), false },
        { "a timeout among the causes", new InvalidOperationException("wrapped", new TimeoutException("too slow")), true },
        {
            "an HTTP 503 two causes down",
            new InvalidOperationException("outer", new InvalidOperationException("middle", Http(HttpStatusCode.ServiceUnavailable))),
            true
        },
        {
            "a transient failure among an aggregate's causes (not the first)",
            new AggregateException(new InvalidOperationException("broken"), new TransientFailureException([Unreachable])),
            true
        },
        {
            "a transient failure under an aggregate's cause",
            new AggregateException(new InvalidOperationException("wrapped", new TimeoutException("too slow"))),
            true
        },
        { "an aggregate of bugs", new AggregateException(new InvalidOperationException("one"), new ArgumentException("two")), false },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void IsTransient_ShouldTellWhetherAnotherTryMayPass(string description, Exception failure, bool transient)
        => TransientFailures.IsTransient(failure).ShouldBe(transient, description);

    [Fact]
    public void IsTransient_ShouldAskTheCallersRuleAboutEveryCause()
    {
        var failure = new InvalidOperationException("wrapped", new AggregateException(new IOException("the connection broke")));

        TransientFailures.IsTransient(failure).ShouldBeFalse();
        TransientFailures.IsTransient(failure, exception => exception is IOException).ShouldBeTrue();
    }

    [Fact]
    public void IsTransient_ShouldNotTakeTransienceAway_WhenTheCallersRuleSaysNo()
        => TransientFailures.IsTransient(new TimeoutException("too slow"), _ => false).ShouldBeTrue();

    [Fact]
    public void IsTransient_ShouldEnd_WhenAnAggregateHoldsTheSameCauseMoreThanOnce()
    {
        var bug = new InvalidOperationException("broken");
        var aggregate = new AggregateException(bug, bug, bug);

        TransientFailures.IsTransient(aggregate).ShouldBeFalse();
    }

    [Fact]
    public void TryGetErrors_ShouldFindTheErrorsOfATransientFailureAmongTheCauses()
    {
        var failure = new InvalidOperationException("wrapped", new TransientFailureException([Unreachable, NotFound]));

        TransientFailures.TryGetErrors(failure, out var errors).ShouldBeTrue();

        errors.ShouldBe([Unreachable, NotFound]);
    }

    [Fact]
    public void TryGetErrors_ShouldFindNone_WithoutATransientFailureHoldingErrors()
    {
        TransientFailures.TryGetErrors(new TransientFailureException("down"), out var errors).ShouldBeFalse();
        errors.ShouldBeEmpty();
        TransientFailures.TryGetErrors(new TimeoutException("too slow"), out _).ShouldBeFalse();
    }

    [Fact]
    public void ThrowIfTransient_ShouldThrowTheErrors_WhenAnyErrorIsTransient()
    {
        var result = Result.Failure<int>([NotFound, Unreachable]);

        var thrown = Should.Throw<TransientFailureException>(() => result.ThrowIfTransient());

        thrown.Errors.ShouldBe([NotFound, Unreachable]);
        thrown.Message.ShouldBe(
            "Failed transiently: Player.NotFound: No such player.; SwgohApi.Unreachable: SwgohApi could not be reached.");
    }

    [Fact]
    public void ThrowIfTransient_ShouldReturnTheResult_WhenItIsADefiniteFailure()
    {
        var result = Result.Failure<int>(NotFound);

        result.ThrowIfTransient().ShouldBeSameAs(result);
    }

    [Fact]
    public void ThrowIfTransient_ShouldReturnTheResult_WhenItSucceeded()
    {
        var result = Result.Success(42);

        result.ThrowIfTransient().Value.ShouldBe(42);
        Result.Success().ThrowIfTransient().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ThrowIfTransient_ShouldThrow_ForAResultWithoutAValue()
        => Should.Throw<TransientFailureException>(() => Result.Failure(Unreachable).ThrowIfTransient())
            .Errors.ShouldBe([Unreachable]);

    [Fact]
    public async Task ThrowIfTransient_ShouldThrow_ForAPendingResult()
    {
        var thrown = await Should.ThrowAsync<TransientFailureException>(
            () => Task.FromResult(Result.Failure<int>(Unreachable)).ThrowIfTransient());
        thrown.Errors.ShouldBe([Unreachable]);

        await Should.ThrowAsync<TransientFailureException>(
            () => Task.FromResult(Result.Failure(Unreachable)).ThrowIfTransient());
    }

    [Fact]
    public async Task ThrowIfTransient_ShouldChainAPendingResult_WhenItIsNotTransient()
    {
        var result = await Task.FromResult(Result.Success(7)).ThrowIfTransient();

        result.Value.ShouldBe(7);
    }

    [Fact]
    public void TransientFailureException_ShouldKeepItsErrors_AndItsOwnMessage()
    {
        var inner = new HttpRequestException("refused");

        var exception = new TransientFailureException("GuildMemberJoined: SwgohApi.GetPlayer", [Unreachable], inner);

        exception.Message.ShouldBe("GuildMemberJoined: SwgohApi.GetPlayer");
        exception.Errors.ShouldBe(ImmutableArray.Create(Unreachable));
        exception.InnerException.ShouldBeSameAs(inner);
        new TransientFailureException().Errors.ShouldBeEmpty();
        new TransientFailureException("down", inner).Errors.ShouldBeEmpty();
    }

    private static HttpRequestException Http(HttpStatusCode status)
        => new("the downstream service answered", null, status);

    private static DbException Database(bool transient)
    {
        var exception = Substitute.For<DbException>();
        exception.IsTransient.Returns(transient);
        return exception;
    }
}
