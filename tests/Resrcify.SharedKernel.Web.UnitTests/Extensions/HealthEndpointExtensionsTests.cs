using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.Health;
using Resrcify.SharedKernel.Web.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class HealthEndpointExtensionsTests
{
    [Fact]
    public async Task MapHealthEndpoints_ShouldRunEveryCheck_OnHealth()
    {
        await using var app = await StartAsync();

        var (status, checks) = await GetAsync(app, "/health");

        status.ShouldBe(HttpStatusCode.OK);
        checks.ShouldBe(["cache", "database", "jobs"], ignoreOrder: true);
    }

    [Fact]
    public async Task MapHealthEndpoints_ShouldRunTheReadyChecks_OnHealthReady()
    {
        await using var app = await StartAsync();

        var (status, checks) = await GetAsync(app, "/health/ready");

        status.ShouldBe(HttpStatusCode.OK);
        checks.ShouldBe(["database"]);
    }

    [Fact]
    public async Task MapHealthEndpoints_ShouldRunTheLiveChecks_OnHealthLive()
    {
        await using var app = await StartAsync();

        var (status, checks) = await GetAsync(app, "/health/live");

        status.ShouldBe(HttpStatusCode.OK);
        checks.ShouldBe(["jobs"]);
    }

    [Fact]
    public async Task MapHealthEndpoints_ShouldAnswerServiceUnavailable_WhenAReadyCheckIsUnhealthy()
    {
        await using var app = await StartAsync(databaseHealthy: false);

        (await GetAsync(app, "/health/ready")).Status.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await GetAsync(app, "/health")).Status.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await GetAsync(app, "/health/live")).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MapHealthEndpoints_ShouldAnswerHealthyOnHealthLive_WhenNoCheckIsTaggedLive()
    {
        await using var app = await TestHosts.StartAsync(
            services => services.AddHealthChecks().AddCheck("database", () => HealthCheckResult.Healthy(), [HealthTags.Ready]),
            app => app.MapHealthEndpoints());

        var (status, checks) = await GetAsync(app, "/health/live");

        status.ShouldBe(HttpStatusCode.OK);
        checks.ShouldBeEmpty();
    }

    [Fact]
    public async Task MapHealthEndpoints_ShouldAnswerAnonymousCallers_WhenEveryOtherEndpointNeedsAUser()
    {
        await using var app = await TestHosts.StartAsync(
            services =>
            {
                AddChecks(services, databaseHealthy: true);
                services.AddAuthentication();
                services.AddAuthorizationBuilder()
                    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
            },
            app =>
            {
                app.UseAuthorization();
                app.MapHealthEndpoints();
            });

        (await GetAsync(app, "/health")).Status.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(app, "/health/ready")).Status.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(app, "/health/live")).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MapHealthEndpoints_ShouldMapTheGivenPaths()
    {
        await using var app = await TestHosts.StartAsync(
            services => AddChecks(services, databaseHealthy: true),
            app => app.MapHealthEndpoints(paths =>
            {
                paths.Path = "/status";
                paths.ReadyPath = "/status/readiness";
                paths.LivePath = "/status/liveness";
            }));

        (await GetAsync(app, "/status")).Checks.Length.ShouldBe(3);
        (await GetAsync(app, "/status/readiness")).Checks.ShouldBe(["database"]);
        (await GetAsync(app, "/status/liveness")).Checks.ShouldBe(["jobs"]);
    }

    private static Task<WebApplication> StartAsync(
        bool databaseHealthy = true)
        => TestHosts.StartAsync(
            services => AddChecks(services, databaseHealthy),
            app => app.MapHealthEndpoints());

    private static void AddChecks(
        IServiceCollection services,
        bool databaseHealthy)
        => services
            .AddHealthChecks()
            .AddCheck(
                "database",
                () => databaseHealthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("down"),
                [HealthTags.Ready])
            .AddCheck("jobs", () => HealthCheckResult.Healthy(), [HealthTags.Live])
            .AddCheck("cache", () => HealthCheckResult.Healthy());

    private static async Task<(HttpStatusCode Status, string[] Checks)> GetAsync(
        WebApplication app,
        string path)
    {
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var checks = body.RootElement
            .GetProperty("checks")
            .EnumerateObject()
            .Select(check => check.Name)
            .ToArray();
        return (response.StatusCode, checks);
    }
}
