using System;
using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.ArchitectureTesting.Handlers;
using Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Tests;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Handlers;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MediatorRequestHandlersTests
{
    [Theory]
    [InlineData(typeof(SampleHandlers.SenderClassicCommandHandler))]
    [InlineData(typeof(SampleHandlers.MediatorClassicQueryHandler))]
    [InlineData(typeof(SampleHandlers.LazySenderRequestHandler))]
    [InlineData(typeof(SampleHandlers.SenderScatterGatherQueryHandler))]
    [InlineData(typeof(SampleHandlers.CreateSample.Handler))]
    [InlineData(typeof(SampleHandlers.PublisherCommandHandler))]
    public void IsRequestHandler_ShouldBeTrue_ForACommandQueryRequestOrScatterGatherRequestHandler(Type type)
        => MediatorRequestHandlers.IsRequestHandler(type).ShouldBeTrue();

    [Theory]
    [InlineData(typeof(SampleHandlers.SenderIntegrationEventHandler))]
    [InlineData(typeof(SampleHandlers.SenderPipelineBehavior<,>))]
    [InlineData(typeof(SampleHandlers.SendingCommandHandlerBase))]
    [InlineData(typeof(SampleHandlers.NestedSenderCommandHandler.Forwarder))]
    public void IsRequestHandler_ShouldBeFalse_ForWhatIsNotAConcreteRequestHandler(Type type)
        => MediatorRequestHandlers.IsRequestHandler(type).ShouldBeFalse();

    [Fact]
    public void SenderDependencies_ShouldNameTheConstructorParameterAndTheField_OfAClassicConstructor()
        => MediatorRequestHandlers
            .SenderDependencies(typeof(SampleHandlers.SenderClassicCommandHandler))
            .ShouldBe(
            [
                "constructor parameter 'sender' (ISender)",
                "field '_sender' (ISender)",
            ]);

    [Fact]
    public void SenderDependencies_ShouldNameOnlyTheParameter_OfAPrimaryConstructor()
        => MediatorRequestHandlers
            .SenderDependencies(typeof(SampleHandlers.MediatorPrimaryQueryHandler))
            .ShouldBe(["constructor parameter 'mediator' (IMediator)"]);

    [Fact]
    public void SenderDependencies_ShouldNameTheBaseClassMember_OfADerivedHandler()
        => MediatorRequestHandlers
            .SenderDependencies(typeof(SampleHandlers.DerivedSenderCommandHandler))
            .ShouldBe(
            [
                "constructor parameter 'sender' (ISender)",
                "property 'BaseSender' (ISender)",
            ]);

    [Fact]
    public void SenderDependencies_ShouldBeEmpty_WhenTheHandlerOnlyPublishes()
        => MediatorRequestHandlers
            .SenderDependencies(typeof(SampleHandlers.PublisherCommandHandler))
            .ShouldBeEmpty();

    [Fact]
    public void SenderDependencies_ShouldBeEmpty_WhenOnlyANestedTypeSends()
        => MediatorRequestHandlers
            .SenderDependencies(typeof(SampleHandlers.NestedSenderCommandHandler))
            .ShouldBeEmpty();
}
