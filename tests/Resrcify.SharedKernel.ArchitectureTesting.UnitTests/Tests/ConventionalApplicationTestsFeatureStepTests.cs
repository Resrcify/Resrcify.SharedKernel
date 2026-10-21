using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.ArchitectureTesting.Layering;
using Resrcify.SharedKernel.ArchitectureTesting.Tests;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Tests;

/// <summary>
/// <c>FeatureSteps_Should_BeInternalSealed_AndNotDependOnSenderMediatorOrHandlers</c> on this test assembly, standing
/// in for a service's Application layer: its <see cref="SampleFeatureSteps"/> break the convention and keep it, and
/// <see cref="PublicStep"/> is visible outside the assembly.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ConventionalApplicationTestsFeatureStepTests
{
    private static Assembly WithSamples
        => typeof(SampleFeatureSteps).Assembly;

    [Theory]
    [InlineData(nameof(SampleFeatureSteps.UnsealedStep), "is not sealed")]
    [InlineData(nameof(SampleFeatureSteps.SenderStep), "constructor parameter 'sender' (ISender)")]
    [InlineData(nameof(SampleFeatureSteps.MediatorStep), "constructor parameter 'mediator' (IMediator)")]
    [InlineData(nameof(SampleFeatureSteps.HandlerStep), "constructor parameter 'handler' (ICommandHandler<SampleCommand>)")]
    [InlineData(nameof(SampleFeatureSteps.ConcreteHandlerStep), "constructor parameter 'handler' (PublisherCommandHandler)")]
    public void FeatureStepRule_ShouldNameTheStepAndWhatBreaksTheConvention(
        string step,
        string violation)
    {
        var rules = new ApplicationLayerTests(WithSamples);

        var failure = Should.Throw<ShouldAssertException>(rules.FeatureSteps_Should_BeInternalSealed_AndNotDependOnSenderMediatorOrHandlers);

        failure.Message.ShouldContain($"{Layers.Application}: {typeof(SampleFeatureSteps).FullName}+{step} → {violation}");
    }

    [Fact]
    public void FeatureStepRule_ShouldNameAPublicStep()
    {
        var rules = new ApplicationLayerTests(WithSamples);

        var failure = Should.Throw<ShouldAssertException>(rules.FeatureSteps_Should_BeInternalSealed_AndNotDependOnSenderMediatorOrHandlers);

        failure.Message.ShouldContain($"{typeof(PublicStep).FullName} → is public (a step is internal)");
    }

    [Theory]
    [InlineData(nameof(SampleFeatureSteps.LoggingStep))]
    [InlineData(nameof(SampleFeatureSteps.DerivedFromUnsealedStep))]
    [InlineData(nameof(SampleFeatureSteps.StepBase))]
    public void FeatureStepRule_ShouldNotName_AStepKeepingTheConvention_OrAnAbstractOne(string step)
    {
        var rules = new ApplicationLayerTests(WithSamples);

        var failure = Should.Throw<ShouldAssertException>(rules.FeatureSteps_Should_BeInternalSealed_AndNotDependOnSenderMediatorOrHandlers);

        failure.Message.ShouldNotContain($"+{step} ");
    }

    [Fact]
    public void FeatureStepRule_ShouldPass_WhenTheAssemblyHasNoStep()
        => new ApplicationLayerTests(typeof(Result).Assembly).FeatureSteps_Should_BeInternalSealed_AndNotDependOnSenderMediatorOrHandlers();

    /// <summary>A step visible outside its assembly: the convention wants it internal.</summary>
    public sealed class PublicStep : IFeatureStep
    {
        private readonly Result _result = Result.Success();

        public Result Run()
            => _result;
    }

    private sealed class ApplicationLayerTests(
        Assembly applicationAssembly)
        : ConventionalApplicationTests(new Dictionary<string, Assembly> { [Layers.Application] = applicationAssembly });
}
