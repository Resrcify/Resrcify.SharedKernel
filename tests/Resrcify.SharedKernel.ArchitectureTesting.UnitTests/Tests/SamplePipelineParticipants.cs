using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;
using static Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Tests.SampleHandlers;

namespace Resrcify.SharedKernel.ArchitectureTesting.UnitTests.Tests;

/// <summary>
/// The validators and behaviors <c>Validators_Should_NotDependOnSenderOrMediator</c> and
/// <c>Behaviors_Should_NotDependOnSenderOrMediator</c> are tested on: the first group of each sends (each must be
/// named), the second doesn't, or isn't checked (none may be named).
/// </summary>
internal static class SamplePipelineParticipants
{
    internal sealed record Rename(string Name)
        : ICommand;

    // ─────────────────────── Validators that send ───────────────────────

    internal sealed class SenderValidator : AbstractValidator<Rename>
    {
        public SenderValidator(ISender sender)
            => RuleFor(rename => rename.Name)
                .MustAsync(async (name, cancellationToken) =>
                    (await sender.Send(new SampleRequest(), cancellationToken)).IsSuccess && name.Length > 0);
    }

    internal sealed class MediatorValidator : AbstractValidator<Rename>
    {
        public MediatorValidator(IMediator mediator)
            => RuleFor(rename => rename.Name)
                .MustAsync(async (name, cancellationToken) =>
                    (await mediator.Send(new SampleRequest(), cancellationToken)).IsSuccess && name.Length > 0);
    }

    internal sealed class LazySenderValidator : AbstractValidator<Rename>
    {
        public LazySenderValidator(Lazy<ISender> sender)
            => RuleFor(rename => rename.Name)
                .MustAsync(async (name, cancellationToken) =>
                    (await sender.Value.Send(new SampleRequest(), cancellationToken)).IsSuccess && name.Length > 0);
    }

    // A validator that implements IValidator<T> itself, not through AbstractValidator<T>.
    internal sealed class HandWrittenSenderValidator(
        Func<ISender> sender)
        : IValidator<Rename>
    {
        public Func<ISender> Sender
            => sender;

        public FluentValidation.Results.ValidationResult Validate(Rename instance)
            => new();

        public Task<FluentValidation.Results.ValidationResult> ValidateAsync(Rename instance, CancellationToken cancellation = default)
            => Task.FromResult(new FluentValidation.Results.ValidationResult());

        public FluentValidation.Results.ValidationResult Validate(IValidationContext context)
            => new();

        public Task<FluentValidation.Results.ValidationResult> ValidateAsync(IValidationContext context, CancellationToken cancellation = default)
            => Task.FromResult(new FluentValidation.Results.ValidationResult());

        public IValidatorDescriptor CreateDescriptor()
            => throw new NotSupportedException();

        public bool CanValidateInstancesOfType(Type type)
            => type == typeof(Rename);
    }

    // ─────────────── Validators that don't send, and an abstract base ───────────────

    internal sealed class PlainValidator : AbstractValidator<Rename>
    {
        public PlainValidator()
            => RuleFor(rename => rename.Name).NotEmpty();
    }

    internal sealed class PublisherValidator : AbstractValidator<Rename>
    {
        public PublisherValidator(IPublisher publisher)
            => RuleFor(rename => rename.Name)
                .MustAsync(async (name, cancellationToken) =>
                {
                    await publisher.Publish(new SampleNotification(), cancellationToken);
                    return name.Length > 0;
                });
    }

    // Abstract: not built by the container, so not checked (a concrete subclass is, with what it inherits).
    internal abstract class SendingValidatorBase : AbstractValidator<Rename>
    {
        protected SendingValidatorBase(ISender sender)
            => Sender = sender;

        protected ISender Sender { get; }
    }

    // ─────────────────────── Behaviors that send ───────────────────────

    internal sealed class MediatorPreProcessor<TRequest>(
        IMediator mediator)
        : IRequestPreProcessor<TRequest>
        where TRequest : notnull
    {
        public Task Process(TRequest request, CancellationToken cancellationToken)
            => mediator.Send(new SampleRequest(), cancellationToken);
    }

    internal sealed class SenderPostProcessor(
        IEnumerable<ISender> senders)
        : IRequestPostProcessor<Rename, Result>
    {
        public async Task Process(Rename request, Result response, CancellationToken cancellationToken)
        {
            foreach (var sender in senders)
                await sender.Send(new SampleRequest(), cancellationToken);
        }
    }

    // ─────────────────────── Behaviors that don't ───────────────────────

    internal sealed class PublisherBehavior<TRequest, TResponse>(
        IPublisher publisher)
        : IPipelineBehavior<TRequest, TResponse>
    {
        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            await publisher.Publish(new SampleNotification(), cancellationToken);
            return await next(cancellationToken);
        }
    }
}
