using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.IntegrationTesting.TestService;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Factories;

/// <summary>
/// Two hosts of the same service, with their own containers, alive at once: with the settings in process-wide
/// environment variables the second would have overwritten the first's.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ServiceHostFixtureTests(
    FullServiceHostFixture full,
    DatabaseOnlyServiceHostFixture databaseOnly)
    : IClassFixture<FullServiceHostFixture>,
    IClassFixture<DatabaseOnlyServiceHostFixture>
{
    [Fact]
    public void Settings_ShouldDescribeTheStartedContainers_WithTheServicesOwnSettingsOnTop()
    {
        full.Settings["Database:Host"].ShouldBe(full.Postgres.Host);
        full.Settings["Database:Port"].ShouldBe(full.Postgres.Port);
        full.Settings["Database:Database"].ShouldBe(FullServiceHostFixture.Database);
        full.Settings["Database:Username"].ShouldBe(FullServiceHostFixture.DatabaseUser);
        full.Settings["Database:Password"].ShouldBe(FullServiceHostFixture.DatabasePassword);
        full.Settings["MessageBus:Host"].ShouldBe(full.RabbitMq!.Host);
        full.Settings["MessageBus:Port"].ShouldBe(full.RabbitMq.AmqpPort);
        full.Settings["MessageBus:Username"].ShouldBe(FullServiceHostFixture.BusUser);
        full.Settings["MessageBus:Password"].ShouldBe(FullServiceHostFixture.BusPassword);
        full.Settings["Patreon:Token"].ShouldBe("test");
        full.Settings["Migrations:Run"].ShouldBe("false");

        databaseOnly.RabbitMq.ShouldBeNull();
        databaseOnly.Settings["Migrations:Run"].ShouldBe("true");
        databaseOnly.Settings.Keys.ShouldNotContain(key => key.StartsWith("MessageBus:", StringComparison.Ordinal));
        databaseOnly.Settings["Database:Port"].ShouldNotBe(full.Settings["Database:Port"]);
    }

    [Fact]
    public void Program_ShouldSeeItsOwnSettings_WhileItRegistersItsServices()
    {
        AssertSeen(full.Settings, Seen(full));
        AssertSeen(databaseOnly.Settings, Seen(databaseOnly));
        Seen(databaseOnly).Keys.ShouldNotContain("MessageBus:Host");
    }

    [Fact]
    public async Task Program_ShouldReachItsOwnDatabase_WithNothingInTheEnvironment()
    {
        Environment.GetEnvironmentVariable("Database__Host").ShouldBeNull();
        Environment.GetEnvironmentVariable("Database__Port").ShouldBeNull();
        Environment.GetEnvironmentVariable("MessageBus__Host").ShouldBeNull();
        Environment.GetEnvironmentVariable("Migrations__Run").ShouldBeNull();

        using var fullClient = full.CreateClient();
        using var databaseOnlyClient = databaseOnly.CreateClient();

        (await fullClient.GetStringAsync(new Uri("/database", UriKind.Relative)))
            .ShouldBe(FullServiceHostFixture.Database);
        (await databaseOnlyClient.GetStringAsync(new Uri("/database", UriKind.Relative)))
            .ShouldBe(databaseOnly.Settings["Database:Database"]);
    }

    [Fact]
    public async Task ConfigureTestServices_ShouldReplaceTheServicesOwnRegistration()
    {
        using var fullClient = full.CreateClient();
        using var databaseOnlyClient = databaseOnly.CreateClient();

        (await fullClient.GetStringAsync(new Uri("/greeting", UriKind.Relative)))
            .ShouldBe(FullServiceHostFixture.Greeting);
        (await databaseOnlyClient.GetStringAsync(new Uri("/greeting", UriKind.Relative)))
            .ShouldBe("Hello from the service");
    }

    [Fact]
    public async Task Network_ShouldLetTheContainersReachEachOtherByName()
    {
        full.Network.ShouldNotBeNull();
        databaseOnly.Network.ShouldBeNull();

        var postgresFromRabbitMq = await full.RabbitMq!.Container.ExecAsync(
            ["getent", "hosts", FullServiceHostFixture.PostgresNetworkAlias]);
        var rabbitMqFromPostgres = await full.Postgres.Container.ExecAsync(
            ["getent", "hosts", FullServiceHostFixture.RabbitMqNetworkAlias]);

        postgresFromRabbitMq.ExitCode.ShouldBe(0);
        rabbitMqFromPostgres.ExitCode.ShouldBe(0);
    }

    private static IReadOnlyDictionary<string, string?> Seen<TFixture>(TFixture fixture)
        where TFixture : WebApplicationFactory<Program>
        => fixture.Services.GetRequiredService<RegistrationSettings>().Values;

    private static void AssertSeen(
        IReadOnlyDictionary<string, string?> given,
        IReadOnlyDictionary<string, string?> seen)
    {
        foreach (var (key, value) in given.OrderBy(setting => setting.Key, StringComparer.Ordinal))
            seen.ShouldContainKeyAndValue(key, value);
    }
}
