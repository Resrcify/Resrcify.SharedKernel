using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.IntegrationTesting.GenericHostTestService;
using Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.IntegrationTesting.TestService;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Factories;

/// <summary>
/// The settings a factory gives win over the service's <c>appsettings.json</c>, which sets the same key, on both
/// hosting models: <c>Host.CreateDefaultBuilder</c> (where <c>UseSetting</c> alone lost) and <c>WebApplication</c>.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class IntegrationFactoryBaseTests(
    GenericHostServiceHostFixture genericHost,
    DependencyServiceHostFixture webApplication)
    : IClassFixture<GenericHostServiceHostFixture>,
    IClassFixture<DependencyServiceHostFixture>
{
    [Fact]
    public async Task Settings_ShouldWinOverAppSettings_OnTheGenericHost()
    {
        genericHost.Services.GetRequiredService<RegisteredSample>().Value
            .ShouldBe(GenericHostServiceHostFixture.SampleValue);
        genericHost.Services.GetRequiredService<IConfiguration>()["Sample:Value"]
            .ShouldBe(GenericHostServiceHostFixture.SampleValue);

        using var client = genericHost.CreateClient();
        (await client.GetStringAsync(new Uri("/", UriKind.Relative)))
            .ShouldBe(GenericHostServiceHostFixture.SampleValue);
    }

    [Fact]
    public void Settings_ShouldWinOverAppSettings_OnAWebApplication()
    {
        webApplication.Services.GetRequiredService<RegistrationSettings>().Values["Sample:Value"]
            .ShouldBe(DependencyServiceHostFixture.SampleValue);
        webApplication.Services.GetRequiredService<IConfiguration>()["Sample:Value"]
            .ShouldBe(DependencyServiceHostFixture.SampleValue);
    }

    [Fact]
    public void AppSettings_ShouldStillApply_ToWhatTheFactoryDoesNotSet()
        => webApplication.Services.GetRequiredService<IConfiguration>()["Patreon:Token"]
            .ShouldBe("from appsettings.json");
}
