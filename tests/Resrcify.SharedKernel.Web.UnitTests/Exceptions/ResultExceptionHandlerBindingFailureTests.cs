using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Exceptions;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Exceptions;

/// <summary>
/// A minimal-API parameter that fails to bind, answered by <see cref="ResultExceptionHandler"/> as problem details whose
/// one <see cref="ErrorType.Validation"/> error names the parameter, in Production as in Development, and logged as
/// nobody's error.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ResultExceptionHandlerBindingFailureTests
{
    private const string BadGuid = "not-a-guid-7f3a";

    public static TheoryData<string> Environments => [Microsoft.Extensions.Hosting.Environments.Production, Microsoft.Extensions.Hosting.Environments.Development];

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task AMissingRequiredQueryParameter_ShouldBeA400NamingIt(
        string environment)
    {
        using var logs = new CapturingLoggerProvider();
        await using var app = await StartAsync(logs, environment);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/search", UriKind.Relative));

        var error = await SingleValidationErrorAsync(response, HttpStatusCode.BadRequest);
        error.Code.ShouldBe("page");
        error.Message.ShouldBe("The required parameter \"page\" was not provided from query string.");
        ShouldLogNoError(logs);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task AnInvalidRouteGuid_ShouldBeA400NamingIt_WithoutTheValueSent(
        string environment)
    {
        using var logs = new CapturingLoggerProvider();
        await using var app = await StartAsync(logs, environment);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri($"/shards/{BadGuid}", UriKind.Relative));

        var error = await SingleValidationErrorAsync(response, HttpStatusCode.BadRequest);
        error.Code.ShouldBe("id");
        error.Message.ShouldBe("The value of parameter \"id\" is not valid.");
        error.Message.ShouldNotContain(BadGuid);
        error.Message.ShouldNotContain("Guid");
        ShouldLogNoError(logs);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task MalformedJson_ShouldBeA400NamingTheBodyParameter(
        string environment)
    {
        using var logs = new CapturingLoggerProvider();
        await using var app = await StartAsync(logs, environment);
        using var client = app.GetTestClient();
        using var body = new StringContent("{ \"name\": ", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/shards", UriKind.Relative), body);

        var error = await SingleValidationErrorAsync(response, HttpStatusCode.BadRequest);
        error.Code.ShouldBe("request");
        error.Message.ShouldBe("The parameter \"request\" couldn't be read from the request body.");
        error.Message.ShouldNotContain(nameof(CreateShardRequest));
        ShouldLogNoError(logs);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task ABodyTooLarge_ShouldBeA413_AsTheExceptionSays(
        string environment)
    {
        // As Kestrel throws it while the body is read for a parameter.
        using var logs = new CapturingLoggerProvider();
        await using var app = await StartAsync(logs, environment);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/too-large", UriKind.Relative));

        var error = await SingleValidationErrorAsync(response, HttpStatusCode.RequestEntityTooLarge);
        error.Code.ShouldBe(ResultExceptionHandler.BadRequestErrorCode);
        error.Message.ShouldBe("The request is too large.");
        ShouldLogNoError(logs);
    }

    [Fact]
    public async Task TheDetail_ShouldHoldAspNetCoresMessage_InDevelopmentOnly()
    {
        using var logs = new CapturingLoggerProvider();
        await using var production = await StartAsync(logs, Microsoft.Extensions.Hosting.Environments.Production);
        await using var development = await StartAsync(logs, Microsoft.Extensions.Hosting.Environments.Development);

        var hidden = await DetailAsync(production);
        var shown = await DetailAsync(development);

        hidden.ShouldBeNull();
        shown.ShouldNotBeNull().ShouldContain("int page");
    }

    [Fact]
    public async Task ABindingFailure_ShouldBeAnEmpty400InProduction_WhenTurnedOff()
    {
        using var logs = new CapturingLoggerProvider();
        await using var app = await StartAsync(
            logs,
            Microsoft.Extensions.Hosting.Environments.Production,
            options => options.ProblemDetailsForBindingFailures = false);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/search", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task AValidRequest_ShouldStillBeAnswered()
    {
        using var logs = new CapturingLoggerProvider();
        await using var app = await StartAsync(logs, Microsoft.Extensions.Hosting.Environments.Production);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/search?page=2", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("page 2");
    }

    private static async Task<Error> SingleValidationErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");
        var result = await response.ToResultAsync();
        var error = result.Errors.ShouldHaveSingleItem();
        error.Type.ShouldBe(ErrorType.Validation);
        return error;
    }

    private static void ShouldLogNoError(
        CapturingLoggerProvider logs)
        => logs.Entries.Where(entry => entry.Level >= LogLevel.Warning).ShouldBeEmpty();

    private static async Task<string?> DetailAsync(
        WebApplication app)
    {
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(new Uri("/search", UriKind.Relative));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("detail", out var detail)
            ? detail.GetString()
            : null;
    }

    private static Task<WebApplication> StartAsync(
        CapturingLoggerProvider logs,
        string environment,
        Action<ResultProblemDetailsOptions>? configure = null)
        => TestHosts.StartAsync(
            services =>
            {
                services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug));
                services.AddSingleton<ILoggerProvider>(logs);
                services.AddResultProblemDetails(configure);
            },
            app =>
            {
                app.UseExceptionHandler();
                app.MapGet("/search", (int page) => $"page {page}");
                app.MapGet("/shards/{id}", (Guid id) => id.ToString());
                app.MapPost("/shards", (CreateShardRequest request) => request.Name);
                app.MapGet("/too-large", string () => throw new BadHttpRequestException(
                    "Request body too large. The max request body size is 10 bytes.",
                    StatusCodes.Status413PayloadTooLarge));
            },
            environment);

    internal sealed record CreateShardRequest(string Name);
}
