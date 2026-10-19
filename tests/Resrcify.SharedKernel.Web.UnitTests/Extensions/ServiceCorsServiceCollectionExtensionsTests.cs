using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.UnitTests.Hosting;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ServiceCorsServiceCollectionExtensionsTests
{
    private const string Policy = "ShardCors";

    [Fact]
    public async Task ACrossOriginRequest_ShouldBeAllowedFromAnyOrigin_WithoutCredentials()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/ping", UriKind.Relative));
        request.Headers.Add("Origin", "https://front-end.example");

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldHaveSingleItem().ShouldBe("*");
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }

    [Fact]
    public async Task APreflight_ShouldAllowAnyMethodAndHeader()
    {
        await using var app = await StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, new Uri("/ping", UriKind.Relative));
        request.Headers.Add("Origin", "https://front-end.example");
        request.Headers.Add("Access-Control-Request-Method", "DELETE");
        request.Headers.Add("Access-Control-Request-Headers", "x-custom");

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Methods").ShouldHaveSingleItem().ShouldBe("DELETE");
        response.Headers.GetValues("Access-Control-Allow-Headers").ShouldHaveSingleItem().ShouldBe("x-custom");
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }

    [Fact]
    public void AddServiceCors_ShouldRefuseAnEmptyPolicyName()
        => Should.Throw<ArgumentException>(() => new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddServiceCors(" "));

    private static Task<WebApplication> StartAsync()
        => TestHosts.StartAsync(
            services => services.AddServiceCors(Policy),
            app =>
            {
                app.UseCors(Policy);
                app.MapGet("/ping", () => TypedResults.Ok("pong"));
                app.MapDelete("/ping", () => TypedResults.NoContent());
            });
}
