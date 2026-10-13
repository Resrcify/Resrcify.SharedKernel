using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Mediator.Diagnostics;

/// <summary>
/// The mediator's traces and metrics, for every request sent (<c>Send</c>), whatever behaviors are registered.
/// <para>
/// Traces (<c>.AddSource(MediatorDiagnostics.ActivitySourceName)</c>): one span per request, named after the request
/// type, tagged <c>mediator.request.type</c> (its full name) and, when it ends, <c>mediator.outcome</c>
/// (<c>success</c>, <c>failure</c> or <c>exception</c>); a failure also gets <c>mediator.error.code</c> and
/// <c>mediator.error.type</c> (its first error's), and an exception is recorded on the span. The span's status is
/// <c>Error</c> for an exception and for a failure another try might fix (<see cref="ErrorTypeExtensions.IsTransient"/>);
/// a failure about the request (not found, invalid, ...) is an expected answer and leaves it unset.
/// </para>
/// <para>
/// Metrics (<c>.AddMeter(MediatorDiagnostics.MeterName)</c>): <c>mediator.request.duration</c> (s), how long a
/// request took, tagged <c>request</c> (the request type's name) and <c>outcome</c>: <c>success</c>, the
/// <see cref="ErrorType"/> of a failure's first error (<c>NotFound</c>, <c>Validation</c>, ...), or <c>exception</c>.
/// </para>
/// <para>
/// When nothing listens (no trace listener for the source, no meter listener for the histogram), a send costs one
/// check: no span, no clock reading, no tags.
/// </para>
/// </summary>
public static class MediatorDiagnostics
{
    public const string ActivitySourceName = "Resrcify.SharedKernel.Mediator";

    public const string MeterName = "Resrcify.SharedKernel.Mediator";

    internal const string SuccessOutcome = "success";

    internal const string FailureOutcome = "failure";

    internal const string ExceptionOutcome = "exception";

    internal static ActivitySource ActivitySource { get; } = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "mediator.request.duration",
        unit: "s",
        description: "How long a request sent through the mediator took, by request and outcome.");

    /// <summary>Whether anything listens: a trace listener for the source, or a meter listener for the duration.</summary>
    internal static bool IsObserved
        => ActivitySource.HasListeners() || Duration.Enabled;

    /// <summary>Starts the span of a request; <see langword="null"/> when no trace listener samples it.</summary>
    internal static Activity? StartSend(Type requestType)
    {
        var activity = ActivitySource.StartActivity(requestType.Name, ActivityKind.Internal);
        activity?.SetTag("mediator.request.type", requestType.FullName);
        return activity;
    }

    /// <summary>Records a request that returned <paramref name="response"/>.</summary>
    internal static void RecordSent(
        Activity? activity,
        Type requestType,
        object? response,
        TimeSpan elapsed)
    {
        if (response is Result { IsFailure: true } failure)
        {
            var error = failure.Errors[0];
            activity?.SetTag("mediator.outcome", FailureOutcome);
            activity?.SetTag("mediator.error.code", error.Code);
            activity?.SetTag("mediator.error.type", error.Type.ToString());
            if (failure.Errors.Any(e => e.IsTransient()))
                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
            RecordDuration(requestType, error.Type.ToString(), elapsed);
            return;
        }

        activity?.SetTag("mediator.outcome", SuccessOutcome);
        RecordDuration(requestType, SuccessOutcome, elapsed);
    }

    /// <summary>Records a request that threw <paramref name="exception"/>.</summary>
    internal static void RecordThrown(
        Activity? activity,
        Type requestType,
        Exception exception,
        TimeSpan elapsed)
    {
        if (activity is not null)
        {
            activity.SetTag("mediator.outcome", ExceptionOutcome);
            activity.AddException(exception);
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        }

        RecordDuration(requestType, ExceptionOutcome, elapsed);
    }

    private static void RecordDuration(Type requestType, string outcome, TimeSpan elapsed)
    {
        if (!Duration.Enabled)
            return;

        Duration.Record(
            elapsed.TotalSeconds,
            new TagList
            {
                { "request", requestType.Name },
                { "outcome", outcome },
            });
    }
}
