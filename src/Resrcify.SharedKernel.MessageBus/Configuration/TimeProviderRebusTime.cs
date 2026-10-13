using System;
using Rebus.Time;

namespace Resrcify.SharedKernel.MessageBus.Configuration;

/// <summary>Rebus' clock, read from a <see cref="TimeProvider"/> (the container's).</summary>
internal sealed class TimeProviderRebusTime(TimeProvider time) : IRebusTime
{
    public DateTimeOffset Now => time.GetUtcNow();
}
