using System;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;

/// <summary>
/// Event whose handler persists a side effect (a <see cref="TestAggregate"/>) and can
/// optionally fail afterwards — used to prove the handler's work and the outbox
/// "mark processed" update commit or roll back together.
/// </summary>
internal sealed record TestSideEffectEvent(
    Guid Id,
    Guid AggregateId,
    bool ShouldThrow)
    : DomainEvent(Id);

internal sealed class TestSideEffectEventHandler(TestDbContext context)
    : INotificationHandler<TestSideEffectEvent>
{
    public async Task Handle(
        TestSideEffectEvent notification,
        CancellationToken cancellationToken)
    {
        // Persisted through the same DbContext the outbox job marks the message on.
        context.Aggregates.Add(
            new TestAggregate(notification.AggregateId, "written-by-handler"));

        await Task.CompletedTask;

        if (notification.ShouldThrow)
            throw new InvalidOperationException(
                "handler failed after writing an aggregate");
    }
}
