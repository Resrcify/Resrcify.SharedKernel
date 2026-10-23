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
/// <c>Validators_Should_NotDependOnSenderOrMediator</c> and <c>Behaviors_Should_NotDependOnSenderOrMediator</c> on this
/// test assembly, standing in for a service's Application layer: its <see cref="SamplePipelineParticipants"/> send from
/// inside the pipeline, and don't.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ConventionalApplicationTestsPipelineTests
{
    private static Assembly WithSamples
        => typeof(SamplePipelineParticipants).Assembly;

    private static string Participant(string name)
        => $"{Layers.Application}: {typeof(SamplePipelineParticipants).FullName}+{name}";

    [Theory]
    [InlineData(nameof(SamplePipelineParticipants.SenderValidator), "constructor parameter 'sender' (ISender)")]
    [InlineData(nameof(SamplePipelineParticipants.MediatorValidator), "constructor parameter 'mediator' (IMediator)")]
    [InlineData(nameof(SamplePipelineParticipants.LazySenderValidator), "constructor parameter 'sender' (Lazy<ISender>)")]
    [InlineData(nameof(SamplePipelineParticipants.HandWrittenSenderValidator), "constructor parameter 'sender' (Func<ISender>)")]
    [InlineData(nameof(SamplePipelineParticipants.HandWrittenSenderValidator), "property 'Sender' (Func<ISender>)")]
    public void ValidatorRule_ShouldNameTheValidatorAndWhatSends(
        string validator,
        string dependency)
    {
        var rules = new ApplicationLayerTests(WithSamples);

        var failure = Should.Throw<ShouldAssertException>(rules.Validators_Should_NotDependOnSenderOrMediator);

        failure.Message.ShouldContain($"{Participant(validator)} → {dependency}");
    }

    [Theory]
    [InlineData(nameof(SamplePipelineParticipants.PlainValidator))]
    [InlineData(nameof(SamplePipelineParticipants.PublisherValidator))]
    [InlineData(nameof(SamplePipelineParticipants.SendingValidatorBase))]
    [InlineData(nameof(SampleHandlers.SenderClassicCommandHandler))]
    public void ValidatorRule_ShouldNotName_AValidatorThatDoesntSend_AnAbstractOne_OrWhatIsNotAValidator(string type)
    {
        var rules = new ApplicationLayerTests(WithSamples);

        var failure = Should.Throw<ShouldAssertException>(rules.Validators_Should_NotDependOnSenderOrMediator);

        failure.Message.ShouldNotContain($"+{type} ");
    }

    [Theory]
    [InlineData("MediatorPreProcessor`1", "constructor parameter 'mediator' (IMediator)")]
    [InlineData(nameof(SamplePipelineParticipants.SenderPostProcessor), "constructor parameter 'senders' (IEnumerable<ISender>)")]
    public void BehaviorRule_ShouldNameTheBehaviorAndWhatSends(
        string behavior,
        string dependency)
    {
        var rules = new ApplicationLayerTests(WithSamples);

        var failure = Should.Throw<ShouldAssertException>(rules.Behaviors_Should_NotDependOnSenderOrMediator);

        failure.Message.ShouldContain($"{Participant(behavior)} → {dependency}");
    }

    [Fact]
    public void BehaviorRule_ShouldNameAPipelineBehaviorThatSends()
    {
        var rules = new ApplicationLayerTests(WithSamples);

        var failure = Should.Throw<ShouldAssertException>(rules.Behaviors_Should_NotDependOnSenderOrMediator);

        failure.Message.ShouldContain($"{typeof(SampleHandlers).FullName}+SenderPipelineBehavior`2 → constructor parameter 'sender' (ISender)");
    }

    [Theory]
    [InlineData("PublisherBehavior`2")]
    [InlineData("HandlerInjectingPipelineBehavior`2")]
    [InlineData(nameof(SampleHandlers.SenderClassicCommandHandler))]
    public void BehaviorRule_ShouldNotName_ABehaviorThatDoesntSend_OrWhatIsNotABehavior(string type)
    {
        var rules = new ApplicationLayerTests(WithSamples);

        var failure = Should.Throw<ShouldAssertException>(rules.Behaviors_Should_NotDependOnSenderOrMediator);

        failure.Message.ShouldNotContain($"+{type} ");
    }

    [Fact]
    public void BothRules_ShouldPass_WhenTheAssemblyHasNoValidatorOrBehavior()
    {
        var rules = new ApplicationLayerTests(typeof(Result).Assembly);

        rules.Validators_Should_NotDependOnSenderOrMediator();
        rules.Behaviors_Should_NotDependOnSenderOrMediator();
    }

    private sealed class ApplicationLayerTests(
        Assembly applicationAssembly)
        : ConventionalApplicationTests(new Dictionary<string, Assembly> { [Layers.Application] = applicationAssembly });
}
