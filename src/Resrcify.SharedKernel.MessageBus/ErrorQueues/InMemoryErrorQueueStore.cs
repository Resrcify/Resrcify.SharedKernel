using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Messages;
using Rebus.Transport.InMem;

namespace Resrcify.SharedKernel.MessageBus.ErrorQueues;

/// <summary>
/// The error queue on the in-memory network (<c>UseInMemory</c>, the test harness). A scan takes every message out and
/// puts the kept ones back in their order.
/// </summary>
internal sealed class InMemoryErrorQueueStore(InMemNetwork network) : IErrorQueueStore
{
    public Task<int> CountAsync(
        string queue,
        CancellationToken cancellationToken)
        => Task.FromResult(network.HasQueue(queue) ? network.Count(queue) : 0);

    public Task<IReadOnlyList<ScanResult>> ScanAsync(
        string queue,
        int limit,
        Func<StoredMessage, ScanStep> decide,
        CancellationToken cancellationToken)
    {
        if (!network.HasQueue(queue))
            return Task.FromResult<IReadOnlyList<ScanResult>>([]);

        var results = new List<ScanResult>();
        var kept = new List<InMemTransportMessage>();
        try
        {
            var count = network.Count(queue);
            for (var read = 0; read < count; read++)
            {
                if (network.GetNextOrNull(queue) is not { } next)
                    break;

                if (read >= limit)
                {
                    kept.Add(next);
                    continue;
                }

                var message = new StoredMessage(next.Headers, next.Body);
                var step = decide(message);
                switch (step.Action)
                {
                    case ScanAction.Delete:
                        results.Add(new ScanResult(message, step.Action));
                        break;
                    case ScanAction.MoveBack when network.HasQueue(step.Destination):
                        var moved = new TransportMessage(StoredMessageHeaders.WithoutErrorHeaders(next.Headers), next.Body);
                        network.Deliver(step.Destination, new InMemTransportMessage(moved));
                        results.Add(new ScanResult(message, step.Action));
                        break;
                    case ScanAction.MoveBack:
                        kept.Add(next);
                        results.Add(new ScanResult(message, step.Action, $"queue {step.Destination} doesn't exist"));
                        break;
                    default:
                        kept.Add(next);
                        break;
                }
            }
        }
        finally
        {
            foreach (var message in kept)
                network.Deliver(queue, message, alwaysQuiet: true);
        }

        return Task.FromResult<IReadOnlyList<ScanResult>>(results);
    }
}
