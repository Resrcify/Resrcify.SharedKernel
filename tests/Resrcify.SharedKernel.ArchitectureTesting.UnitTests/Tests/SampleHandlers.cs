using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Tests;

/// <summary>
/// The handlers <c>Handlers_Should_NotDependOnSenderOrMediator</c> is tested on: this test assembly stands in for a
/// service's layer. The first group sends a request (each must be named), the second doesn't, or isn't a request
/// handler (none may be named).
/// </summary>
internal static class SampleHandlers
{
    internal sealed record SampleCommand
        : ICommand;

    internal sealed record SampleQuery
        : IQuery<SampleResponse>;

    internal sealed record SampleResponse(int Value);

    internal sealed record SampleRequest
        : IRequest<Result>;

    internal sealed record SampleScatterRequest
        : IRequest<Result<SampleResponse>>;

    internal sealed record SampleItemRequest(string Key);

    internal sealed record SampleItemResponse(string Key);

    internal sealed record SampleNotification
        : INotification;

    internal sealed record SampleIntegrationEvent;

    // ─────────────────────── Handlers that send ───────────────────────

    internal sealed class SenderClassicCommandHandler
        : ICommandHandler<SampleCommand>
    {
        private readonly ISender _sender;

        public SenderClassicCommandHandler(ISender sender)
            => _sender = sender;

        public Task<Result> Handle(
            SampleCommand request,
            CancellationToken cancellationToken)
            => _sender.Send(new SampleRequest(), cancellationToken);
    }

    internal sealed class SenderPrimaryCommandHandler(
        ISender sender)
        : ICommandHandler<SampleCommand>
    {
        public Task<Result> Handle(
            SampleCommand request,
            CancellationToken cancellationToken)
            => sender.Send(new SampleRequest(), cancellationToken);
    }

    internal sealed class MediatorClassicQueryHandler
        : IQueryHandler<SampleQuery, SampleResponse>
    {
        private readonly IMediator _mediator;

        public MediatorClassicQueryHandler(IMediator mediator)
            => _mediator = mediator;

        public async Task<Result<SampleResponse>> Handle(
            SampleQuery request,
            CancellationToken cancellationToken)
        {
            await _mediator.Send(new SampleRequest(), cancellationToken);
            return Result.Success(new SampleResponse(1));
        }
    }

    internal sealed class MediatorPrimaryQueryHandler(
        IMediator mediator)
        : IQueryHandler<SampleQuery, SampleResponse>
    {
        public async Task<Result<SampleResponse>> Handle(
            SampleQuery request,
            CancellationToken cancellationToken)
        {
            await mediator.Send(new SampleRequest(), cancellationToken);
            return Result.Success(new SampleResponse(1));
        }
    }

    internal sealed class LazySenderRequestHandler(
        Lazy<ISender> sender)
        : IRequestHandler<SampleRequest, Result>
    {
        public Task<Result> Handle(
            SampleRequest request,
            CancellationToken cancellationToken)
            => sender.Value.Send(new SampleRequest(), cancellationToken);
    }

    internal sealed class SenderPropertyCommandHandler
        : ICommandHandler<SampleCommand>
    {
        public required ISender Sender { get; init; }

        public Task<Result> Handle(
            SampleCommand request,
            CancellationToken cancellationToken)
            => Sender.Send(new SampleRequest(), cancellationToken);
    }

    internal abstract class SendingCommandHandlerBase(
        ISender sender)
    {
        protected ISender BaseSender { get; } = sender;
    }

    internal sealed class DerivedSenderCommandHandler(
        ISender sender)
        : SendingCommandHandlerBase(sender),
        ICommandHandler<SampleCommand>
    {
        public Task<Result> Handle(
            SampleCommand request,
            CancellationToken cancellationToken)
            => BaseSender.Send(new SampleRequest(), cancellationToken);
    }

    // A handler in a static class, named after its use case.
    internal static class CreateSample
    {
        internal sealed class Handler(
            ISender sender)
            : ICommandHandler<SampleCommand>
        {
            public Task<Result> Handle(
                SampleCommand request,
                CancellationToken cancellationToken)
                => sender.Send(new SampleRequest(), cancellationToken);
        }
    }

    internal sealed class SenderScatterGatherQueryHandler(
        ISender sender)
        : IScatterGatherRequestHandler<SampleScatterRequest, SampleItemRequest, SampleItemResponse, Result<SampleResponse>>
    {
        public TimeSpan Timeout
            => TimeSpan.FromSeconds(1);

        public async Task<IReadOnlyDictionary<string, SampleItemRequest>> ScatterAsync(
            SampleScatterRequest request,
            CancellationToken cancellationToken)
        {
            await sender.Send(new SampleRequest(), cancellationToken);
            return new Dictionary<string, SampleItemRequest>(StringComparer.Ordinal);
        }

        public Task<Result<SampleResponse>> HandleAsync(
            SampleScatterRequest request,
            IGathered<SampleItemResponse> replies,
            CancellationToken cancellationToken)
            => Task.FromResult(Result.Success(new SampleResponse(1)));
    }

    // ───────────────── Handlers that don't send, and non-handlers ──────────────────

    internal sealed class PublisherCommandHandler(
        IPublisher publisher)
        : ICommandHandler<SampleCommand>
    {
        public async Task<Result> Handle(
            SampleCommand request,
            CancellationToken cancellationToken)
        {
            await publisher.Publish(new SampleNotification(), cancellationToken);
            return Result.Success();
        }
    }

    internal sealed class SenderIntegrationEventHandler(
        ISender sender)
        : IIntegrationEventHandler<SampleIntegrationEvent>
    {
        public Task<Result> HandleAsync(
            SampleIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
            => sender.Send(new SampleRequest(), cancellationToken);
    }

    internal sealed class SenderPipelineBehavior<TRequest, TResponse>(
        ISender sender)
        : IPipelineBehavior<TRequest, TResponse>
    {
        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            await sender.Send(new SampleRequest(), cancellationToken);
            return await next(cancellationToken);
        }
    }

    // Its nested type sends; the handler itself doesn't.
    internal sealed class NestedSenderCommandHandler
        : ICommandHandler<SampleCommand>
    {
        public Task<Result> Handle(
            SampleCommand request,
            CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());

        internal sealed class Forwarder(
            ISender sender)
        {
            public Task<Result> Forward(CancellationToken cancellationToken)
                => sender.Send(new SampleRequest(), cancellationToken);
        }
    }
}
