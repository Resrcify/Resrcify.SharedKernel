using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.IntegrationTesting.Factories;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;

/// <summary>A service logging through Serilog (<c>AddServiceTelemetry</c>), without a database.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit class fixtures must be public")]
public sealed class SerilogServiceHostFixture
    : ServiceHostFixture<Program>
{
    protected override PostgresContainerFixture? CreatePostgres()
        => null;

    protected override void ConfigureSettings(IDictionary<string, string?> settings)
        => settings["TestService:UseServiceTelemetry"] = "true";
}
