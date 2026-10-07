using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxHealthChecksBuilderExtensionsTests
{
    [Fact]
    public void AddOutbox_ShouldFailAsDegraded_ByDefault()
    {
        // Unhealthy on /health/ready would take every replica out of service at once over a backlog.
        var registration = Registration(builder => builder.AddOutbox<TestDbContext>(tags: ["ready"]));

        registration.FailureStatus.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public void AddOutbox_ShouldFailWithTheGivenStatus_WhenOneIsGiven()
        => Registration(builder => builder.AddOutbox<TestDbContext>(failureStatus: HealthStatus.Unhealthy))
            .FailureStatus.ShouldBe(HealthStatus.Unhealthy);

    private static HealthCheckRegistration Registration(System.Action<IHealthChecksBuilder> add)
    {
        var services = new ServiceCollection();
        add(services.AddHealthChecks());
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.ShouldHaveSingleItem();
    }
}
