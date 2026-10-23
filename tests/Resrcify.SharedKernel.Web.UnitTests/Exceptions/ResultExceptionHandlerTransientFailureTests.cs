using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Exceptions;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.UnitTests.Hosting;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Exceptions;

/// <summary>
/// An exception the transient-failure classifier calls transient: a 503 with <c>Retry-After</c>, logged at Warning, where
/// any other unhandled exception stays a 500.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ResultExceptionHandlerTransientFailureTests
{
    private static readonly Error Unavailable = Error.Failure(
        ErrorCodes.Request.Unavailable,
        ResultExceptionHandler.TransientErrorMessage);

    [Fact]
    public async Task ATransientDatabaseFailure_Should_Be503_WithRetryAfter_ReadBackAsATransientFailure()
    {
        using var logs = new FakeLoggerProvider();
        await using var app = await StartAsync(logs);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/database", UriKind.Relative));
        var result = await response.ToResultAsync<string>();

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(1));
        result.Errors.ShouldBe([Unavailable]);
        result.Errors.ShouldAllBe(error => error.IsTransient());
        var logged = logs.Collector.GetSnapshot().Where(entry => entry.Exception is not null).ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Warning);
        logged.Exception.ShouldBeAssignableTo<DbException>().ShouldNotBeNull().IsTransient.ShouldBeTrue();
        logs.Collector.GetSnapshot().ShouldNotContain(entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task AnExceptionARegisteredRuleCallsTransient_Should_Be503()
    {
        await using var app = await StartAsync(
            services: services => services.AddTransientFailureRule(exception => exception is NotSupportedException));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/unsupported", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task AnExceptionNoRuleCallsTransient_Should_Stay500_WithoutRetryAfter()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/unsupported", UriKind.Relative));
        var result = await response.ToResultAsync<string>();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.RetryAfter.ShouldBeNull();
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ErrorCodes.Request.Unhandled);
    }

    [Fact]
    public async Task TheRetryAfter_Should_BeTheOptionsInWholeSecondsRoundedUp()
    {
        await using var app = await StartAsync(
            configure: options => options.TransientFailureRetryAfter = TimeSpan.FromSeconds(2.5));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/database", UriKind.Relative));

        response.Headers.GetValues("Retry-After").ShouldHaveSingleItem().ShouldBe("3");
    }

    [Fact]
    public async Task ATransientFailureWithErrors_Should_StillBeAnsweredWithThem()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/with-errors", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        response.Headers.RetryAfter.ShouldBeNull();
    }

    [Fact]
    public async Task ATimeout_Should_Be503_ByTheBuiltInRule_WithoutAClassifier()
    {
        using var logs = new FakeLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var context = Context();

        var handled = await Handler(loggerFactory).TryHandleAsync(context, new TimeoutException(), CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().ShouldBe("1");
        logs.Collector.GetSnapshot().ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task ATransientFailureTheMediatorLoggedAlready_Should_GetADebugLine_AndStillBe503()
    {
        using var logs = new FakeLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging
            .SetMinimumLevel(LogLevel.Debug)
            .AddProvider(logs));
        var context = Context();
        var exception = new TimeoutException("The query timed out.");
        LoggedExceptions.Claim(exception);

        await Handler(loggerFactory).TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        var entry = logs.Collector.GetSnapshot().ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Debug);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain(nameof(TimeoutException));
        LoggedExceptions.IsLogged(exception).ShouldBeFalse();
    }

    [Fact]
    public async Task TheContainersClassifier_Should_DecideAlone_WhenItSaysNo()
    {
        var classifier = Substitute.For<ITransientFailureClassifier>();
        classifier.IsTransient(Arg.Any<Exception>()).Returns(false);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var context = Context();

        await Handler(loggerFactory, classifier).TryHandleAsync(context, new TimeoutException(), CancellationToken.None);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
    }

    private static Task<WebApplication> StartAsync(
        FakeLoggerProvider? logs = null,
        Action<ResultProblemDetailsOptions>? configure = null,
        Action<IServiceCollection>? services = null)
        => TestHosts.StartAsync(
            collection =>
            {
                services?.Invoke(collection);
                collection.AddResultProblemDetails(configure);
                if (logs is not null)
                    collection.AddSingleton<ILoggerProvider>(logs);
            },
            app =>
            {
                app.UseExceptionHandler();
                app.MapGet("/database", string () => throw TransientDatabaseFailure());
                app.MapGet("/unsupported", string () => throw new NotSupportedException("The upstream is down."));
                app.MapGet("/with-errors", string () => throw new TransientFailureException(
                    [Error.ExternalFailure("SwgohApi.Unreachable", "SwgohApi could not be reached.")]));
            });

    private static ResultExceptionHandler Handler(
        ILoggerFactory loggerFactory,
        ITransientFailureClassifier? classifier = null)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);

        return new ResultExceptionHandler(
            environment,
            Options.Create(new ResultProblemDetailsOptions()),
            loggerFactory.CreateLogger<ResultExceptionHandler>(),
            classifier);
    }

    private static DefaultHttpContext Context()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();

        return new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
    }

    // A database failure that says another try may get past it (as Npgsql's for a dropped connection).
    private static DbException TransientDatabaseFailure()
    {
        var failure = Substitute.For<DbException>();
        failure.IsTransient.Returns(true);
        return failure;
    }
}
