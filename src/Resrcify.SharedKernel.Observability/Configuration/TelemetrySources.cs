namespace Resrcify.SharedKernel.Observability.Configuration;

/// <summary>
/// The <see cref="System.Diagnostics.ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/> names
/// <c>AddServiceTelemetry</c> subscribes to besides the instrumentation libraries. Each name is both the source's and
/// the meter's.
/// </summary>
public static class TelemetrySources
{
    /// <summary>
    /// Every SharedKernel source and meter: the mediator (<c>MediatorDiagnostics</c>), the outbox
    /// (<c>OutboxDiagnostics</c>) and the message bus (<c>MessageBusDiagnostics</c>) all name theirs
    /// <c>Resrcify.SharedKernel.&lt;package&gt;…</c>, so one wildcard covers them, and any added later.
    /// </summary>
    public const string SharedKernel = "Resrcify.SharedKernel.*";

    /// <summary>
    /// Rebus.Diagnostics (<c>RebusDiagnosticConstants.ActivitySourceName</c> and <c>MeterName</c>): the message bus'
    /// send and handle spans, and Rebus' message metrics. The message bus turns it on for every bus.
    /// </summary>
    public const string RebusDiagnostics = "Rebus.Diagnostics";

    /// <summary>
    /// Quartz 4 (<c>QuartzInstrumentation.ActivitySourceName</c> and <c>MeterName</c>): a span per job run and the
    /// scheduler's metrics. OpenTelemetry's <c>AddQuartzInstrumentation()</c> listens where Quartz 3 wrote, which
    /// Quartz 4 doesn't.
    /// </summary>
    public const string Quartz = "Quartz";
}
