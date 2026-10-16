using System;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.Results.Diagnostics;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>The level the behaviors log an exception at: Warning when another try may get past it, Error otherwise.</summary>
internal static class TransientFailureLevels
{
    /// <summary>
    /// <see cref="LogLevel.Warning"/> when <paramref name="exception"/> is transient (by <paramref name="classifier"/>,
    /// else the built-in rule, <see cref="TransientFailures"/>), <see cref="LogLevel.Error"/> otherwise.
    /// </summary>
    public static LogLevel Of(
        Exception exception,
        ITransientFailureClassifier? classifier)
    {
        var transient = classifier?.IsTransient(exception) ?? TransientFailures.IsTransient(exception);
        return transient
            ? LogLevel.Warning
            : LogLevel.Error;
    }
}
