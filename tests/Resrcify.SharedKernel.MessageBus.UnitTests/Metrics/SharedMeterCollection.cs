using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Metrics;

/// <summary>
/// The test classes that count a message bus measurement tagged only with names other classes use too (e.g.
/// <c>messagebus.events.handled event=PayoutRotated outcome=success</c>), one at a time and with no other class running:
/// the bus' meter is one for the process, and its <c>events.*</c> counters carry no tag of the bus that recorded them, so
/// a class running alongside would add its own measurements to the count (<see cref="MetricsCapture"/>).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit collection definitions must be public")]
public sealed class SharedMeterCollection
{
    public const string Name = nameof(SharedMeterCollection);

    // Only named by [Collection]; xUnit never creates it.
    private SharedMeterCollection()
    {
    }
}
