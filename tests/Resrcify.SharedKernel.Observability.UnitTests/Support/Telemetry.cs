using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Observability.Configuration;
using Resrcify.SharedKernel.Observability.Extensions;

namespace Resrcify.SharedKernel.Observability.UnitTests.Support;

/// <summary>A service provider with <c>AddServiceTelemetry</c>, as a service registers it.</summary>
internal static class Telemetry
{
    public const string ServiceName = "test-service";

    public static ServiceProvider Build(
        Action<ServiceTelemetryOptions>? configure = null,
        Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddServiceTelemetry(
            ServiceName,
            Configuration(settings),
            configure);
        return services.BuildServiceProvider();
    }

    public static IConfiguration Configuration(
        Dictionary<string, string?>? settings = null)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? [])
            .Build();
}
