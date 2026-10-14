using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.IntegrationTesting.Factories;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Resrcify.SharedKernel.IntegrationTesting.GenericHostTestService;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;

/// <summary>
/// A service on the generic host (<c>Host.CreateDefaultBuilder</c> + <c>Startup</c>) without a database, whose
/// <c>appsettings.json</c> sets the setting the fixture gives too.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit class fixtures must be public")]
public sealed class GenericHostServiceHostFixture
    : ServiceHostFixture<Startup>
{
    public const string SampleValue = "from the fixture";

    protected override PostgresContainerFixture? CreatePostgres()
        => null;

    protected override void ConfigureSettings(IDictionary<string, string?> settings)
        => settings["Sample:Value"] = SampleValue;
}
