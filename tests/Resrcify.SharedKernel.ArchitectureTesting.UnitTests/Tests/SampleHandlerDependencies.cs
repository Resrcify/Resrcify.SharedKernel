using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Results.Primitives;
using static Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Tests.SampleHandlers;

namespace Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Tests;

/// <summary>
/// The handlers <c>Handlers_Should_NotDependOnOtherHandlers</c> is tested on: the first group takes another handler in
/// its constructor (each must be named), the second doesn't, or isn't a request handler (none may be named).
/// </summary>
internal static class SampleHandlerDependencies
{
    // ─────────────────────── Handlers that take a handler ───────────────────────

    internal sealed class InterfaceInjectingCommandHandler(
        ICommandHandler<SampleCommand> inner)
        : ICommandHandler<SampleCommand>
    {
        public Task<Result> Handle(
            SampleCommand request,
            CancellationToken cancellationToken)
            => inner.Handle(request, cancellationToken);
    }

    internal sealed class RequestHandlerInjectingQueryHandler(
        IRequestHandler<SampleRequest, Result> inner)
        : IQueryHandler<SampleQuery, SampleResponse>
    {
        public async Task<Result<SampleResponse>> Handle(
            SampleQuery request,
            CancellationToken cancellationToken)
        {
            await inner.Handle(new SampleRequest(), cancellationToken);
            return Result.Success(new SampleResponse(1));
        }
    }

    internal sealed class ConcreteHandlerInjectingCommandHandler
        : ICommandHandler<SampleCommand>
    {
        private readonly PublisherCommandHandler _other;

        public ConcreteHandlerInjectingCommandHandler(PublisherCommandHandler other)
            => _other = other;

        public Task<Result> Handle(
            SampleCommand request,
            CancellationToken cancellationToken)
            => _other.Handle(request, cancellationToken);
    }

    internal sealed class EnumerableInjectingCommandHandler(
        IEnumerable<IRequestHandler<SampleRequest, Result>> handlers)
        : ICommandHandler<SampleCommand>
    {
        public async Task<Result> Handle(
            SampleCommand request,
            CancellationToken cancellationToken)
        {
            foreach (var handler in handlers)
                await handler.Handle(new SampleRequest(), cancellationToken);
            return Result.Success();
        }
    }

    internal sealed class LazyInjectingCommandHandler(
        Lazy<ICommandHandler<SampleCommand>> inner)
        : ICommandHandler<SampleCommand>
    {
        public Task<Result> Handle(
            SampleCommand request,
            CancellationToken cancellationToken)
            => inner.Value.Handle(request, cancellationToken);
    }

    internal sealed class FuncInjectingCommandHandler(
        Func<PublisherCommandHandler> inner)
        : ICommandHandler<SampleCommand>
    {
        public Task<Result> Handle(
            SampleCommand request,
            CancellationToken cancellationToken)
            => inner().Handle(request, cancellationToken);
    }

    // ─────────────────── Handlers that don't, and non-handlers ───────────────────

    // Its own logger names it as a generic argument: not a dependency on a handler.
    internal sealed class LoggingCommandHandler(
        ILogger<LoggingCommandHandler> logger)
        : ICommandHandler<SampleCommand>
    {
        public ILogger Logger
            => logger;

        public Task<Result> Handle(
            SampleCommand request,
            CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    internal sealed class HandlerInjectingIntegrationEventHandler(
        ICommandHandler<SampleCommand> handler)
        : IIntegrationEventHandler<SampleIntegrationEvent>
    {
        public Task<Result> HandleAsync(
            SampleIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
            => handler.Handle(new SampleCommand(), cancellationToken);
    }

    internal sealed class HandlerInjectingPipelineBehavior<TRequest, TResponse>(
        IEnumerable<ICommandHandler<SampleCommand>> handlers)
        : IPipelineBehavior<TRequest, TResponse>
    {
        public IEnumerable<ICommandHandler<SampleCommand>> Handlers
            => handlers;

        public Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }
}
