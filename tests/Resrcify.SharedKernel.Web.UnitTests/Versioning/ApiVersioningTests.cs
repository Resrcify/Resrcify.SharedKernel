using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Asp.Versioning.Builder;
using Asp.Versioning.Conventions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Resrcify.SharedKernel.Web.Versioning;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Versioning;

/// <summary>
/// <see cref="ApiVersioning.VersionSet"/> shared by hosts that build their endpoints at the same time, as tests' hosts
/// do: no endpoint writes to it, so none can corrupt it.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ApiVersioningTests
{
    private const int Hosts = 16;
    private const int EndpointsPerHost = 64;

    [Fact]
    public async Task HostsBuildingTheirEndpointsAtOnce_Should_AllBuildThem_AndLeaveTheSharedSetUnchanged()
    {
        // Every host makes its version set before any maps an endpoint, as hosts starting side by side do, so in 4.10.0
        // all of them shared one set. Then they build their endpoints at the same moment (a barrier), each endpoint
        // advertising its version in that set.
        var apps = Enumerable.Range(0, Hosts).Select(_ => NewApp()).ToList();
        try
        {
            foreach (var app in apps)
                app.CreateApiVersionSet();
            foreach (var app in apps)
                MapEndpoints(app, new ApiVersion(1, 0));

            var counts = await BuildEndpointsAtOnceAsync(apps);

            counts.ShouldAllBe(count => count == EndpointsPerHost);
            AdvertisedVersions(ApiVersioning.VersionSet).ShouldBeEmpty("building an endpoint wrote to the shared set");
        }
        finally
        {
            foreach (var app in apps)
                await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task AnEndpointWithAnotherVersion_Should_BeToldToUseASetOfItsOwn()
    {
        await using var app = NewApp();
        app.CreateApiVersionSet();
        MapEndpoints(app, new ApiVersion(2, 0));

        var failure = Should.Throw<InvalidOperationException>(() => EndpointsOf(app));

        failure.Message.ShouldContain("API version 2.0");
        failure.Message.ShouldContain("app.NewApiVersionSet()");
    }

    private static WebApplication NewApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Services.AddServiceApiVersioning();
        return builder.Build();
    }

    private static void MapEndpoints(
        WebApplication app,
        ApiVersion version)
    {
        for (var endpoint = 0; endpoint < EndpointsPerHost; endpoint++)
        {
            app.MapGet($"/endpoint{endpoint}", () => TypedResults.Ok())
                .WithApiVersionSet(ApiVersioning.VersionSet)
                .HasApiVersion(version);
        }
    }

    // Each host on a thread of its own, all released together.
    private static async Task<int[]> BuildEndpointsAtOnceAsync(List<WebApplication> apps)
    {
        using var barrier = new Barrier(apps.Count);
        var builds = apps
            .Select(app => Task.Factory.StartNew(
                () =>
                {
                    barrier.SignalAndWait();
                    return EndpointsOf(app);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();
        return await Task.WhenAll(builds);
    }

    // Building a host's endpoints runs their conventions (WithApiVersionSet, HasApiVersion).
    private static int EndpointsOf(WebApplication app)
        => ((IEndpointRouteBuilder)app).DataSources.Sum(source => source.Endpoints.Count);

    // What the endpoints advertised in the set, which Asp.Versioning keeps on the set's builder.
    private static ICollection<ApiVersion> AdvertisedVersions(ApiVersionSet set)
    {
        var builder = typeof(ApiVersionSet)
            .GetProperty("Builder", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(set)!;
        return (ICollection<ApiVersion>)typeof(ApiVersionConventionBuilderBase)
            .GetProperty("AdvertisedVersions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(builder)!;
    }
}
