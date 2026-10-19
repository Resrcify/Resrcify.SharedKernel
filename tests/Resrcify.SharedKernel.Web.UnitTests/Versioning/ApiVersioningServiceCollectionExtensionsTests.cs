using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Resrcify.SharedKernel.Web.UnitTests.Hosting;
using Resrcify.SharedKernel.Web.Versioning;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Versioning;

/// <summary>
/// The versioning a service gets, on the in-memory test server: one endpoint at version 1.0, asked for without a version
/// and with version 2.0 where each reader looks.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ApiVersioningServiceCollectionExtensionsTests
{
    [Fact]
    public async Task ARequestWithoutAVersion_ShouldBeVersionOne_AndReportTheSupportedVersions()
    {
        await using var app = await StartAsync(versionReader: null);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/ping", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("api-supported-versions").ShouldHaveSingleItem().ShouldBe("1.0");
    }

    [Fact]
    public async Task TheDefaultReader_ShouldReadTheQueryString_AndIgnoreTheMediaType()
    {
        await using var app = await StartAsync(versionReader: null);
        using var client = app.GetTestClient();

        using var byQuery = await client.GetAsync(new Uri("/ping?api-version=2.0", UriKind.Relative));
        using var byMediaType = await SendWithMediaTypeVersionAsync(client, "2.0");

        byQuery.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        byMediaType.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AMediaTypeReader_ShouldReadTheMediaType_AndIgnoreTheQueryString()
    {
        await using var app = await StartAsync(new MediaTypeApiVersionReader("api-version"));
        using var client = app.GetTestClient();

        using var byQuery = await client.GetAsync(new Uri("/ping?api-version=2.0", UriKind.Relative));
        using var byMediaType = await SendWithMediaTypeVersionAsync(client, "2.0");
        using var versionOne = await SendWithMediaTypeVersionAsync(client, "1.0");

        byQuery.StatusCode.ShouldBe(HttpStatusCode.OK);
        // An unsupported version in the media type is a 406 (Asp.Versioning's answer), in the query string a 400.
        byMediaType.StatusCode.ShouldBe(HttpStatusCode.NotAcceptable);
        versionOne.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheOpenApiDocument_ShouldBePerVersion_AndTakeTheConfiguration()
    {
        await using var app = await StartAsync(
            versionReader: null,
            configureOpenApi: options => options.Document.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Configured";
                return Task.CompletedTask;
            }));
        using var client = app.GetTestClient();

        var document = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        document.ShouldContain("\"/ping\"");
        document.ShouldContain("\"title\": \"Configured\"");
    }

    private static async Task<HttpResponseMessage> SendWithMediaTypeVersionAsync(
        HttpClient client,
        string version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/ping", UriKind.Relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json")
        {
            Parameters = { new NameValueHeaderValue("api-version", version) },
        });
        return await client.SendAsync(request);
    }

    private static Task<WebApplication> StartAsync(
        IApiVersionReader? versionReader,
        Action<Asp.Versioning.OpenApi.VersionedOpenApiOptions>? configureOpenApi = null)
        => TestHosts.StartAsync(
            services => services.AddServiceApiVersioning(versionReader, configureOpenApi),
            app =>
            {
                app.CreateApiVersionSet();
                app.MapGet("/ping", () => TypedResults.Ok("pong"))
                    .WithApiVersionSet(ApiVersioning.VersionSet)
                    .HasApiVersion(new ApiVersion(1, 0));
                app.MapOpenApi().WithDocumentPerVersion();
            });
}
