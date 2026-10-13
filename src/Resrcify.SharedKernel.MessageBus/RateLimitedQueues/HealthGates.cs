using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Resrcify.SharedKernel.MessageBus.RateLimitedQueues;

/// <summary>
/// One <see cref="HealthGate"/> per health-check tag, shared by the rate-limited queues gated on it. A gate runs from
/// the first queue that joins it until the last one leaves.
/// </summary>
internal sealed class HealthGates(IServiceProvider serviceProvider, ILogger<HealthGates> logger) : IDisposable
{
    private readonly Dictionary<string, (HealthGate Gate, int Users)> _gates = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public HealthGate Join(string tag, TimeSpan interval)
    {
        lock (_gate)
        {
            if (_gates.TryGetValue(tag, out var joined))
            {
                joined.Gate.UseInterval(interval);
                _gates[tag] = (joined.Gate, joined.Users + 1);
                return joined.Gate;
            }

            var gate = new HealthGate(tag, interval, serviceProvider, logger);
            _gates[tag] = (gate, 1);
            return gate;
        }
    }

    public void Leave(string tag)
    {
        lock (_gate)
        {
            if (!_gates.TryGetValue(tag, out var joined))
                return;
            if (joined.Users > 1)
            {
                _gates[tag] = (joined.Gate, joined.Users - 1);
                return;
            }
            _gates.Remove(tag);
            joined.Gate.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var (gate, _) in _gates.Values)
                gate.Dispose();
            _gates.Clear();
        }
    }
}
