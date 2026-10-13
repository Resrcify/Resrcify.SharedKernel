using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Resrcify.SharedKernel.IntegrationTesting.Factories;
using Resrcify.SharedKernel.IntegrationTesting.Fixtures;
using Resrcify.SharedKernel.IntegrationTesting.TestService;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;

/// <summary>
/// A service as the Titan ones are: its own database and credentials, RabbitMQ, a shared network, a setting and a
/// service of its own.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit class fixtures must be public")]
public sealed class FullServiceHostFixture
    : ServiceHostFixture<Program>
{
    public const string Database = "ServiceDb";
    public const string DatabaseUser = "ServiceUser";
    public const string DatabasePassword = "K33p-Tr4ck!";
    public const string BusUser = "messageUser";
    public const string BusPassword = "supersecretC0de";
    public const string Greeting = "Hello from the test";

    protected override bool UsesNetwork
        => true;

    protected override PostgresContainerFixture CreatePostgres()
        => new ServicePostgres();

    protected override RabbitMqContainerFixture? CreateRabbitMq()
        => new ServiceRabbitMq();

    protected override void ConfigureSettings(IDictionary<string, string?> settings)
    {
        settings["Patreon:Token"] = "test";
        settings["Migrations:Run"] = "false";
    }

    protected override void ConfigureTestServices(IServiceCollection services)
        => services.Replace(ServiceDescriptor.Singleton<IGreeter>(new FixedGreeter()));

    private sealed class ServicePostgres
        : PostgresContainerFixture
    {
        protected override PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
            => builder
                .WithDatabase(Database)
                .WithUsername(DatabaseUser)
                .WithPassword(DatabasePassword);
    }

    private sealed class ServiceRabbitMq
        : RabbitMqContainerFixture
    {
        protected override RabbitMqBuilder Configure(RabbitMqBuilder builder)
            => builder
                .WithUsername(BusUser)
                .WithPassword(BusPassword);
    }

    private sealed class FixedGreeter
        : IGreeter
    {
        public string Greet()
            => Greeting;
    }
}
