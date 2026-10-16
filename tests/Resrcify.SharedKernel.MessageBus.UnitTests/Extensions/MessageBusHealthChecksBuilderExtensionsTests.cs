using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.Abstractions.Health;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageBusHealthChecksBuilderExtensionsTests
{
    [Fact]
    public async Task AddMessageBus_ShouldBeAReadinessCheck_WhenGivenNoTags()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddMessageBus();
        await using var provider = services.BuildServiceProvider();

        Registration(provider).Tags.ShouldBe([HealthTags.Ready]);
    }

    [Fact]
    public async Task AddMessageBus_ShouldKeepTheTagsItIsGiven_NoneIncluded()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddMessageBus(tags: []);
        await using var provider = services.BuildServiceProvider();

        Registration(provider).Tags.ShouldBeEmpty();
    }

    private static HealthCheckRegistration Registration(ServiceProvider provider)
        => provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Single();
}
