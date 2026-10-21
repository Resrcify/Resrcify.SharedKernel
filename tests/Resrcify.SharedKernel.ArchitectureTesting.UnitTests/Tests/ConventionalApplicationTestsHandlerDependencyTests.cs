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
/// <c>Handlers_Should_NotDependOnOtherHandlers</c> on this test assembly, standing in for a service's Infrastructure
/// layer: its <see cref="SampleHandlerDependencies"/> take handlers in constructors, and don't.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ConventionalApplicationTestsHandlerDependencyTests
{
    private static Assembly WithSamples
        => typeof(SampleHandlerDependencies).Assembly;

    [Theory]
    [InlineData(nameof(SampleHandlerDependencies.InterfaceInjectingCommandHandler), "constructor parameter 'inner' (ICommandHandler<SampleCommand>)")]
    [InlineData(nameof(SampleHandlerDependencies.RequestHandlerInjectingQueryHandler), "constructor parameter 'inner' (IRequestHandler<SampleRequest, Result>)")]
    [InlineData(nameof(SampleHandlerDependencies.ConcreteHandlerInjectingCommandHandler), "constructor parameter 'other' (PublisherCommandHandler)")]
    [InlineData(nameof(SampleHandlerDependencies.EnumerableInjectingCommandHandler), "constructor parameter 'handlers' (IEnumerable<IRequestHandler<SampleRequest, Result>>)")]
    [InlineData(nameof(SampleHandlerDependencies.LazyInjectingCommandHandler), "constructor parameter 'inner' (Lazy<ICommandHandler<SampleCommand>>)")]
    [InlineData(nameof(SampleHandlerDependencies.FuncInjectingCommandHandler), "constructor parameter 'inner' (Func<PublisherCommandHandler>)")]
    public void HandlerDependencyRule_ShouldNameTheHandlerAndWhatItTakes_WhenAHandlerTakesAnotherHandler(
        string handler,
        string dependency)
    {
        var rules = new InfrastructureLayerTests(WithSamples);

        var failure = Should.Throw<ShouldAssertException>(rules.Handlers_Should_NotDependOnOtherHandlers);

        failure.Message.ShouldContain($"{Layers.Infrastructure}: {typeof(SampleHandlerDependencies).FullName}+{handler} → {dependency}");
    }

    [Theory]
    [InlineData(nameof(SampleHandlerDependencies.LoggingCommandHandler))]
    [InlineData(nameof(SampleHandlerDependencies.HandlerInjectingIntegrationEventHandler))]
    [InlineData("HandlerInjectingPipelineBehavior`2")]
    [InlineData(nameof(SampleHandlers.PublisherCommandHandler))]
    public void HandlerDependencyRule_ShouldNotName_AHandlerTakingNoHandler_OrWhatIsNotARequestHandler(string type)
    {
        var rules = new InfrastructureLayerTests(WithSamples);

        var failure = Should.Throw<ShouldAssertException>(rules.Handlers_Should_NotDependOnOtherHandlers);

        failure.Message.ShouldNotContain($"+{type} ");
    }

    [Fact]
    public void HandlerDependencyRule_ShouldPass_WhenNoHandlerTakesAnother()
        => new InfrastructureLayerTests(typeof(Result).Assembly).Handlers_Should_NotDependOnOtherHandlers();

    private sealed class InfrastructureLayerTests(
        Assembly infrastructureAssembly)
        : ConventionalApplicationTests(new Dictionary<string, Assembly> { [Layers.Infrastructure] = infrastructureAssembly });
}
