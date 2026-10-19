using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Resrcify.SharedKernel.ArchitectureTesting.Layering;
using Resrcify.SharedKernel.ArchitectureTesting.Tests;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Tests;

/// <summary>
/// <c>Types_Should_NotTakeIConfigurationInAConstructor</c> on this test assembly, standing in for a service's
/// Infrastructure layer: its <see cref="SampleConfigurationReaders"/> take configuration in constructors and where it
/// belongs.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ConventionalConfigurationTestsTests
{
    private static Assembly WithSampleReaders
        => typeof(SampleConfigurationReaders).Assembly;

    [Theory]
    [InlineData(nameof(SampleConfigurationReaders.ConfigurationReadingService), "constructor parameter 'configuration' (IConfiguration)")]
    [InlineData(nameof(SampleConfigurationReaders.SectionReadingService), "constructor parameter 'section' (IConfigurationSection)")]
    [InlineData(nameof(SampleConfigurationReaders.PrivatelyConstructedService), "constructor parameter 'configuration' (IConfiguration)")]
    public void ConfigurationRule_ShouldNameTheTypeAndItsParameter_WhenAConstructorTakesConfiguration(
        string type,
        string parameter)
    {
        var rules = new InfrastructureLayerTests(WithSampleReaders);

        var failure = Should.Throw<ShouldAssertException>(rules.Types_Should_NotTakeIConfigurationInAConstructor);

        failure.Message.ShouldContain($"{Layers.Infrastructure}: {typeof(SampleConfigurationReaders).FullName}+{type} → {parameter}");
    }

    [Theory]
    [InlineData(nameof(SampleConfigurationReaders.OptionsReadingService))]
    [InlineData(nameof(SampleConfigurationReaders))]
    public void ConfigurationRule_ShouldNotName_OptionsOrARegistrationExtension(string type)
    {
        var rules = new InfrastructureLayerTests(WithSampleReaders);

        var failure = Should.Throw<ShouldAssertException>(rules.Types_Should_NotTakeIConfigurationInAConstructor);

        failure.Message.ShouldNotContain($"+{type} ");
        failure.Message.ShouldNotContain($"{type} →");
    }

    [Fact]
    public void ConfigurationRule_ShouldPass_WhenNoConstructorTakesConfiguration()
        => new InfrastructureLayerTests(typeof(Result).Assembly).Types_Should_NotTakeIConfigurationInAConstructor();

    private sealed class InfrastructureLayerTests(
        Assembly infrastructureAssembly)
        : ConventionalConfigurationTests(new Dictionary<string, Assembly> { [Layers.Infrastructure] = infrastructureAssembly });
}
