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
/// <c>Handlers_Should_NotDependOnSenderOrMediator</c> on this test assembly, standing in for a service's
/// Infrastructure layer (any layer is checked): its <see cref="SampleHandlers"/> hold handlers that send and handlers
/// (and non-handlers) that don't.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ConventionalApplicationTestsTests
{
    private static Assembly WithSampleHandlers
        => typeof(SampleHandlers).Assembly;

    private static Assembly WithoutHandlers
        => typeof(Result).Assembly;

    [Theory]
    [InlineData(nameof(SampleHandlers.SenderClassicCommandHandler), "constructor parameter 'sender' (ISender)")]
    [InlineData(nameof(SampleHandlers.SenderClassicCommandHandler), "field '_sender' (ISender)")]
    [InlineData(nameof(SampleHandlers.SenderPrimaryCommandHandler), "constructor parameter 'sender' (ISender)")]
    [InlineData(nameof(SampleHandlers.MediatorClassicQueryHandler), "constructor parameter 'mediator' (IMediator)")]
    [InlineData(nameof(SampleHandlers.MediatorPrimaryQueryHandler), "constructor parameter 'mediator' (IMediator)")]
    [InlineData(nameof(SampleHandlers.LazySenderRequestHandler), "constructor parameter 'sender' (Lazy<ISender>)")]
    [InlineData(nameof(SampleHandlers.SenderPropertyCommandHandler), "property 'Sender' (ISender)")]
    [InlineData(nameof(SampleHandlers.DerivedSenderCommandHandler), "property 'BaseSender' (ISender)")]
    [InlineData($"{nameof(SampleHandlers.CreateSample)}+{nameof(SampleHandlers.CreateSample.Handler)}", "constructor parameter 'sender' (ISender)")]
    [InlineData(nameof(SampleHandlers.SenderScatterGatherQueryHandler), "constructor parameter 'sender' (ISender)")]
    public void HandlerRule_ShouldNameTheHandlerAndWhatSends_WhenAHandlerDependsOnSenderOrMediator(
        string handler,
        string dependency)
    {
        var rules = new InfrastructureLayerTests(WithSampleHandlers);

        var failure = Should.Throw<ShouldAssertException>(rules.Handlers_Should_NotDependOnSenderOrMediator);

        failure.Message.ShouldContain($"{Layers.Infrastructure}: {typeof(SampleHandlers).FullName}+{handler} → {dependency}");
    }

    [Theory]
    [InlineData(nameof(SampleHandlers.PublisherCommandHandler))]
    [InlineData(nameof(SampleHandlers.SenderIntegrationEventHandler))]
    [InlineData("SenderPipelineBehavior`2")]
    [InlineData(nameof(SampleHandlers.NestedSenderCommandHandler))]
    [InlineData(nameof(SampleHandlers.SendingCommandHandlerBase))]
    public void HandlerRule_ShouldNotName_WhatIsNotARequestHandlerThatSends(string type)
    {
        var rules = new InfrastructureLayerTests(WithSampleHandlers);

        var failure = Should.Throw<ShouldAssertException>(rules.Handlers_Should_NotDependOnSenderOrMediator);

        failure.Message.ShouldNotContain($"+{type} ");
        failure.Message.ShouldNotContain($"+{type}+");
    }

    [Fact]
    public void HandlerRule_ShouldNameTheCompilerGeneratedFieldOfAPrimaryConstructorOnlyThroughItsParameter()
    {
        var rules = new InfrastructureLayerTests(WithSampleHandlers);

        var failure = Should.Throw<ShouldAssertException>(rules.Handlers_Should_NotDependOnSenderOrMediator);

        failure.Message.ShouldNotContain("field '<");
    }

    [Fact]
    public void HandlerRule_ShouldPass_WhenNoLayerHasAHandlerThatSends()
        => new InfrastructureLayerTests(WithoutHandlers).Handlers_Should_NotDependOnSenderOrMediator();

    private sealed class InfrastructureLayerTests(
        Assembly infrastructureAssembly)
        : ConventionalApplicationTests(new Dictionary<string, Assembly> { [Layers.Infrastructure] = infrastructureAssembly });
}
