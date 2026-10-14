using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Resrcify.SharedKernel.Observability.UnitTests.Support;

/// <summary>
/// The tests that start tracer providers or serve requests, one at a time: a tracer provider listens to every
/// activity of its sources in the process (ASP.NET Core's included), so another test's requests would reach its
/// exporter.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit collection definitions must be public")]
public sealed class TelemetryCollection
{
    public const string Name = nameof(TelemetryCollection);

    // Only named by [Collection]; xUnit never creates it.
    private TelemetryCollection()
    {
    }
}
