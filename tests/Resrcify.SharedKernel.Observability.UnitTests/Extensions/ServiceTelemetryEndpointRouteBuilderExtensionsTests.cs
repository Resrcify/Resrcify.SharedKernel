using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Observability.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Observability.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ServiceTelemetryEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public async Task MapServiceMetrics_ShouldServeTheSharedKernelMetricsToPrometheus()
    {
        await using var app = await StartAsync(app => app.MapServiceMetrics());
        using var meter = new Meter("Resrcify.SharedKernel.ScrapeTest");
        meter.CreateCounter<long>("scrape_test.requests").Add(3);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/metrics", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("scrape_test_requests_total");
    }

    [Fact]
    public async Task MapServiceMetrics_ShouldServeThePath_WhenOneIsGiven()
    {
        await using var app = await StartAsync(app => app.MapServiceMetrics("/internal/metrics"));
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(new Uri("/internal/metrics", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MapServiceMetrics_ShouldAllowAnonymousCallers()
    {
        await using var app = await StartAsync(app => app.MapServiceMetrics());

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(route => route.RoutePattern.RawText == "/metrics");

        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();
    }

    private static async Task<WebApplication> StartAsync(
        Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddServiceTelemetry(
            "test-service",
            builder.Configuration,
            options =>
            {
                options.Logging = false;
                options.Tracing = false;
            });
        var app = builder.Build();
        map(app);
        await app.StartAsync();
        return app;
    }
}
