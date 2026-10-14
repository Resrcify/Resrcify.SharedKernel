using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Resrcify.SharedKernel.IntegrationTesting.Factories;

/// <summary>
/// Base <see cref="WebApplicationFactory{TEntryPoint}"/> for integration tests.
/// Subclasses supply settings via <see cref="EnvOverrides"/>; the base hands them to the host so that
/// <c>Program.cs</c> sees them while it registers its services, and so that they win over every other source
/// (<c>appsettings*.json</c>, environment variables, the command line, the <c>Program</c>'s own sources). The fixture
/// itself does not own any containers — container fixtures are registered separately as collection fixtures so
/// their lifetime is shared across tests (or see <see cref="ServiceHostFixture{TProgram}"/>, which owns them).
/// </summary>
/// <remarks>
/// The settings go in twice. <c>UseSetting</c> puts them in the host configuration, which a
/// <c>WebApplication.CreateBuilder</c> program reads from the start (<c>builder.Configuration</c>, while it registers).
/// On the generic host (<c>Host.CreateDefaultBuilder</c>) the host configuration comes before the app configuration,
/// so <c>appsettings.json</c> and environment variables override it; an in-memory source added last
/// (<c>ConfigureAppConfiguration</c>) makes them win there, and in the final configuration of both.
/// </remarks>
public abstract class IntegrationFactoryBase<TProgram>
    : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected abstract IDictionary<string, string?> EnvOverrides();

    protected virtual string Environment => "Development";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);

        var settings = EnvOverrides();
        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
    }
}
