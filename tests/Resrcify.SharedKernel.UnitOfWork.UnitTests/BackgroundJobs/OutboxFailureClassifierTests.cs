using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.BackgroundJobs;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxFailureClassifierTests
{
    public static TheoryData<string, Exception, bool> Failures => new()
    {
        { "HTTP 503", Http(HttpStatusCode.ServiceUnavailable), true },
        { "HTTP 500", Http(HttpStatusCode.InternalServerError), true },
        { "HTTP 408", Http(HttpStatusCode.RequestTimeout), true },
        { "HTTP 429", Http(HttpStatusCode.TooManyRequests), true },
        { "HTTP 400", Http(HttpStatusCode.BadRequest), false },
        { "HTTP 404", Http(HttpStatusCode.NotFound), false },
        { "an HTTP request that got no answer", new HttpRequestException("no answer"), true },
        { "a timeout", new TimeoutException("too slow"), true },
        { "a cancellation", new OperationCanceledException("cancelled"), true },
        { "a task cancellation (an HttpClient timeout)", new TaskCanceledException("timed out"), true },
        { "a transient database failure", Database(transient: true), true },
        { "a database failure", Database(transient: false), false },
        { "a serialization failure", DbExceptions.WithSqlState("40001"), true },
        { "a deadlock", DbExceptions.WithSqlState("40P01"), true },
        { "a concurrency conflict", new DbUpdateConcurrencyException("stale"), true },
        { "a unique violation", DbExceptions.WithSqlState("23505"), false },
        { "Polly's timeout", new TimeoutRejectedException(), true },
        { "Polly's open circuit", new BrokenCircuitException(), true },
        { "a handler's bug", new InvalidOperationException("broken"), false },
        { "a timeout among the causes", new InvalidOperationException("wrapped", new TimeoutException("too slow")), true },
        { "an HTTP 503 among the causes", new InvalidOperationException("wrapped", Http(HttpStatusCode.ServiceUnavailable)), true },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void IsTransient_ShouldTellWhetherAnotherTryMayPass(string description, Exception failure, bool transient)
        => OutboxFailureClassifier.IsTransient(failure).ShouldBe(transient, description);

    private static HttpRequestException Http(HttpStatusCode status)
        => new("the downstream service answered", null, status);

    private static DbException Database(bool transient)
    {
        var exception = Substitute.For<DbException>();
        exception.IsTransient.Returns(transient);
        return exception;
    }
}
