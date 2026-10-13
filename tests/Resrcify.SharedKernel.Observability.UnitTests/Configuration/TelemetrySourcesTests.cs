using System;
using System.Diagnostics.CodeAnalysis;
using Quartz.Diagnostics;
using Rebus.Diagnostics;
using Resrcify.SharedKernel.Mediator.Diagnostics;
using Resrcify.SharedKernel.MessageBus.Diagnostics;
using Resrcify.SharedKernel.Observability.Configuration;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Observability.UnitTests.Configuration;

/// <summary>The names are literals (the package references none of these): they must stay what the libraries publish.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class TelemetrySourcesTests
{
    [Fact]
    public void SharedKernel_ShouldMatchEverySharedKernelSourceAndMeter()
    {
        string[] names =
        [
            MediatorDiagnostics.ActivitySourceName,
            MediatorDiagnostics.MeterName,
            OutboxDiagnostics.ActivitySourceName,
            OutboxDiagnostics.MeterName,
            MessageBusDiagnostics.MeterName,
        ];

        names.ShouldAllBe(name => name.StartsWith(TelemetrySources.SharedKernel.TrimEnd('*'), StringComparison.Ordinal));
    }

    [Fact]
    public void RebusDiagnostics_ShouldBeRebusDiagnosticsSourceAndMeter()
    {
        TelemetrySources.RebusDiagnostics.ShouldBe(RebusDiagnosticConstants.ActivitySourceName);
        TelemetrySources.RebusDiagnostics.ShouldBe(RebusDiagnosticConstants.MeterName);
    }

    [Fact]
    public void Quartz_ShouldBeQuartzSourceAndMeter()
    {
        TelemetrySources.Quartz.ShouldBe(QuartzInstrumentation.ActivitySourceName);
        TelemetrySources.Quartz.ShouldBe(QuartzInstrumentation.MeterName);
    }
}
