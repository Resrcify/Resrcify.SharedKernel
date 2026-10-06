using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Resrcify.SharedKernel.Web.UnitTests.Support;

/// <summary>A minimal web application on the in-memory test server.</summary>
internal static class TestHosts
{
    public static async Task<WebApplication> StartAsync(
        Action<IServiceCollection> services,
        Action<WebApplication> app,
        string environment = "Production")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        services(builder.Services);

        var application = builder.Build();
        app(application);
        await application.StartAsync();
        return application;
    }
}
