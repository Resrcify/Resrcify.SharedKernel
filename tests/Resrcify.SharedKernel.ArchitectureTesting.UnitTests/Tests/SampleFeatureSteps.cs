using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;
using static Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Tests.SampleHandlers;

namespace Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Tests;

/// <summary>
/// The steps <c>FeatureSteps_Should_BeInternalSealed_AndNotDependOnSenderMediatorOrHandlers</c> is tested on: the first
/// group breaks the convention (each must be named), the second keeps it (none may be named).
/// </summary>
internal static class SampleFeatureSteps
{
    // ─────────────────────── Steps that break the convention ───────────────────────

    internal class UnsealedStep : IFeatureStep
    {
        public virtual Result Run()
            => Result.Success();
    }

    // Sealed and internal itself: only its base breaks the convention.
    internal sealed class DerivedFromUnsealedStep : UnsealedStep
    {
        public override Result Run()
            => Result.Success();
    }

    internal sealed class SenderStep(
        ISender sender)
        : IFeatureStep
    {
        public Task<Result> RunAsync(CancellationToken cancellationToken)
            => sender.Send(new SampleRequest(), cancellationToken);
    }

    internal sealed class MediatorStep(
        IMediator mediator)
        : IFeatureStep
    {
        public Task<Result> RunAsync(CancellationToken cancellationToken)
            => mediator.Send(new SampleRequest(), cancellationToken);
    }

    internal sealed class HandlerStep(
        ICommandHandler<SampleCommand> handler)
        : IFeatureStep
    {
        public Task<Result> RunAsync(CancellationToken cancellationToken)
            => handler.Handle(new SampleCommand(), cancellationToken);
    }

    internal sealed class ConcreteHandlerStep(
        PublisherCommandHandler handler)
        : IFeatureStep
    {
        public Task<Result> RunAsync(CancellationToken cancellationToken)
            => handler.Handle(new SampleCommand(), cancellationToken);
    }

    // ─────────────────────── Steps that keep it ───────────────────────

    internal sealed class LoggingStep(
        ILogger<LoggingStep> logger,
        IPublisher publisher)
        : IFeatureStep
    {
        public ILogger Logger
            => logger;

        public async Task<Result> RunAsync(CancellationToken cancellationToken)
        {
            await publisher.Publish(new SampleNotification(), cancellationToken);
            return Result.Success();
        }
    }

    // Abstract: not a step the container builds, so not checked (its sealed subclasses are).
    internal abstract class StepBase : IFeatureStep
    {
        public abstract Result Run();
    }
}
