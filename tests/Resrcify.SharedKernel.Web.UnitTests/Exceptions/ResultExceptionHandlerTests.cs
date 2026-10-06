using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Exceptions;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Exceptions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ResultExceptionHandlerTests
{
    private const string Secret = "connection string with a password";

    [Fact]
    public async Task TryHandleAsync_ShouldBeReadBackAsTheUnhandledFailure_WhenTheCallerReadsTheResponseAsAResult()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/boom", UriKind.Relative));
        var result = await response.ToResultAsync<string>();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([Error.Failure(ResultExceptionHandler.ErrorCode, ResultExceptionHandler.ErrorMessage)]);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldAnswerWhatAFailedResultAnswers()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        var thrown = await ProblemAsync(client, "/boom");
        var returned = await ProblemAsync(client, "/failure");

        thrown.ShouldBe(returned);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldNotShowTheException_OutsideDevelopment()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/boom", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(Secret);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task TryHandleAsync_ShouldShowTheException_InDevelopment()
    {
        await using var app = await StartAsync(environment: Environments.Development);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/boom", UriKind.Relative));
        var result = await response.ToResultAsync();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(ResultExceptionHandler.ErrorCode);
        error.Message.ShouldBe($"InvalidOperationException: {Secret}");
        body.RootElement.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain(nameof(ThrowingEndpoint));
    }

    [Fact]
    public async Task TryHandleAsync_ShouldNotShowTheException_WhenTurnedOffInDevelopment()
    {
        await using var app = await StartAsync(
            environment: Environments.Development,
            configure: options => options.IncludeExceptionDetails = false);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/boom", UriKind.Relative));

        (await response.Content.ReadAsStringAsync()).ShouldNotContain(Secret);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldLogTheExceptionOnce()
    {
        using var logs = new CapturingLoggerProvider();
        await using var app = await StartAsync(logs: logs);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/boom", UriKind.Relative));

        var logged = logs.Entries.Where(entry => entry.Exception is not null).ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Error);
        logged.Category.ShouldBe(typeof(ResultExceptionHandler).FullName);
        logged.Exception.ShouldBeOfType<InvalidOperationException>();
        logs.Entries.Count(entry => entry.Level == LogLevel.Error).ShouldBe(1);
        LoggedExceptions.IsLogged(logged.Exception).ShouldBeTrue();
    }

    /// <summary>
    /// A command whose handler throws, sent from an endpoint through the mediator's standard behaviors: the
    /// unit-of-work behavior sees the exception first and logs it at Error; the logging behavior and this handler, which
    /// answers it, log a Debug line without the stack.
    /// </summary>
    [Fact]
    public async Task TryHandleAsync_ShouldNotLogTheExceptionAgain_WhenTheMediatorsBehaviorsLoggedIt()
    {
        using var logs = new CapturingLoggerProvider();
        await using var app = await StartWithMediatorAsync(logs);
        using var client = app.GetTestClient();

        var answered = await ProblemAsync(client, "/command");

        var error = logs.Entries.Where(entry => entry.Level >= LogLevel.Error).ShouldHaveSingleItem();
        error.Category.ShouldStartWith(typeof(UnitOfWorkPipelineBehavior<,>).FullName![..^2]);
        error.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe(CommandFailure);
        logs.Entries.Where(entry => entry.Exception is not null).ShouldHaveSingleItem();
        logs.Entries.ShouldContain(entry => entry.Level == LogLevel.Debug
            && entry.Category == typeof(ResultExceptionHandler).FullName
            && entry.Exception == null
            && entry.Message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        // The same 500 as an exception thrown outside the mediator.
        answered.ShouldBe(await ProblemAsync(client, "/boom"));
    }

    [Fact]
    public async Task TryHandleAsync_ShouldLogADebugLineAndStillAnswer500_WhenTheExceptionWasLoggedAlready()
    {
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging
            .SetMinimumLevel(LogLevel.Debug)
            .AddProvider(logs));
        var context = Context();
        var exception = new InvalidOperationException("logged where it was thrown");
        LoggedExceptions.Claim(exception);

        var handled = await Handler(loggerFactory).TryHandleAsync(context, exception, CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        var entry = logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Debug);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldContain(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task TryHandleAsync_ShouldAnswer499WithoutAnError_WhenTheClientAborted()
    {
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var context = Context();
        context.RequestAborted = aborted.Token;

        var handled = await Handler(loggerFactory).TryHandleAsync(context, new OperationCanceledException(aborted.Token), CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status499ClientClosedRequest);
        logs.Entries.ShouldAllBe(entry => entry.Level < LogLevel.Warning);
    }

    [Fact]
    public async Task TryHandleAsync_ShouldAnswer500_WhenACancellationWasNotTheClients()
    {
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var context = Context();

        var handled = await Handler(loggerFactory).TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        logs.Entries.ShouldContain(entry => entry.Level == LogLevel.Error);
    }

    private static Task<WebApplication> StartAsync(
        string environment = "Production",
        Action<ResultProblemDetailsOptions>? configure = null,
        CapturingLoggerProvider? logs = null)
        => TestHosts.StartAsync(
            services =>
            {
                services.AddResultProblemDetails(configure);
                if (logs is not null)
                    services.AddSingleton<ILoggerProvider>(logs);
            },
            app =>
            {
                app.UseExceptionHandler();
                app.MapGet("/boom", ThrowingEndpoint);
                app.MapGet("/failure", () => Result
                    .Failure(Error.Failure(ResultExceptionHandler.ErrorCode, ResultExceptionHandler.ErrorMessage))
                    .ToProblemDetails());
            },
            environment);

    private static Task<WebApplication> StartWithMediatorAsync(
        CapturingLoggerProvider logs)
        => TestHosts.StartAsync(
            services =>
            {
                services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug));
                services.AddSingleton<ILoggerProvider>(logs);
                services.AddResultProblemDetails();
                services.AddScoped(_ => Substitute.For<IUnitOfWork>());
                services.AddMediator(cfg => cfg.AddStandardBehaviors());
                services.AddTransient<IRequestHandler<ThrowingCommand, Result>, ThrowingCommandHandler>();
            },
            app =>
            {
                app.UseExceptionHandler();
                app.MapGet("/boom", ThrowingEndpoint);
                app.MapGet("/command", async (ISender sender, CancellationToken cancellationToken) =>
                    (await sender.Send(new ThrowingCommand(), cancellationToken)).ToProblemDetails());
            });

    private static string ThrowingEndpoint()
        => throw new InvalidOperationException(Secret);

    // The problem details' members, all but the trace id (which differs per request).
    private static async Task<string> ProblemAsync(
        HttpClient client,
        string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var members = body.RootElement
            .EnumerateObject()
            .Where(member => member.Name != "traceId")
            .Select(member => $"{member.Name}={member.Value.GetRawText()}");
        return $"{(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType} {string.Join(';', members)}";
    }

    private static ResultExceptionHandler Handler(
        ILoggerFactory loggerFactory)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);

        return new ResultExceptionHandler(
            environment,
            Options.Create(new ResultProblemDetailsOptions()),
            loggerFactory.CreateLogger<ResultExceptionHandler>());
    }

    private const string CommandFailure = "The command handler failed.";

    private sealed record ThrowingCommand
        : ICommand;

    private sealed class ThrowingCommandHandler
        : ICommandHandler<ThrowingCommand>
    {
        public Task<Result> Handle(
            ThrowingCommand request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException(CommandFailure);
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
}
