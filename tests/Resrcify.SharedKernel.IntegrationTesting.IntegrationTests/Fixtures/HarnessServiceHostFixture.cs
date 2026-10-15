using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.IntegrationTesting.Factories;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;

/// <summary>A service whose message bus runs on the test harness: no database, no broker.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit class fixtures must be public")]
public sealed class HarnessServiceHostFixture
    : ServiceHostFixture<Program>
{
    protected override bool UsesMessageBusTestHarness
        => true;

    protected override PostgresContainerFixture? CreatePostgres()
        => null;

    protected override void ConfigureSettings(IDictionary<string, string?> settings)
        => settings["TestService:UseMessageBus"] = "true";
}
