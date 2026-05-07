using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Resrcify.SharedKernel.IntegrationTesting.Factories;

/// <summary>
/// Base <see cref="WebApplicationFactory{TEntryPoint}"/> for integration tests.
/// Subclasses supply env-key overrides via <see cref="EnvOverrides"/>; the
/// base wires those into the host config before <c>Program.cs</c> reads
/// environment variables. The fixture itself does not own any containers —
/// container fixtures are registered separately as collection fixtures so
/// their lifetime is shared across tests.
/// </summary>
public abstract class IntegrationFactoryBase<TProgram>
    : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected abstract IDictionary<string, string?> EnvOverrides();

    protected virtual string Environment => "Development";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        foreach (var (key, value) in EnvOverrides())
            builder.UseSetting(key, value);
    }
}
