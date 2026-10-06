using System;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;

/// <summary>An event whose handler times out (e.g. an HTTP call): it throws a cancellation of its own.</summary>
internal sealed record TestTimeoutEvent(Guid Id) : DomainEvent(Id);

internal sealed class TestTimeoutEventHandler : INotificationHandler<TestTimeoutEvent>
{
    public Task Handle(TestTimeoutEvent notification, CancellationToken cancellationToken)
        => throw new TaskCanceledException("The upstream call timed out.");
}

/// <summary>
/// An event whose handler runs a transactional command (as a handler sending an <c>ITransactionCommand</c> does):
/// it writes an aggregate through <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResponse}"/>, inside the transaction
/// the outbox processes the event in. <see cref="CommandFails"/> makes the command return a failure.
/// </summary>
internal sealed record TestTransactionalCommandEvent(Guid Id, Guid AggregateId, bool CommandFails) : DomainEvent(Id);

internal sealed class TestTransactionalCommandEventHandler(TestDbContext context, IUnitOfWork unitOfWork)
    : INotificationHandler<TestTransactionalCommandEvent>
{
    public async Task Handle(TestTransactionalCommandEvent notification, CancellationToken cancellationToken)
    {
        // Work the handler does itself, before the command: it must survive a failed command.
        context.Aggregates.Add(new TestAggregate(Guid.NewGuid(), "before-command"));

        await unitOfWork.ExecuteInTransactionAsync(
            token =>
            {
                context.Aggregates.Add(new TestAggregate(notification.AggregateId, "written-by-command"));
                return Task.FromResult(notification.CommandFails
                    ? Result.Failure(new Error("Command.Failed", "The command failed.", ErrorType.Failure))
                    : Result.Success());
            },
            cancellationToken: cancellationToken);
    }
}
