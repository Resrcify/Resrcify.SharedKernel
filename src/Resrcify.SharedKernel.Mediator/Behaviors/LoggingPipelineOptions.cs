using System;
using Microsoft.Extensions.Logging;

namespace Resrcify.SharedKernel.Mediator.Behaviors;

/// <summary>
/// How <see cref="LoggingPipelineBehavior{TRequest, TResponse}"/> logs. Set with <c>ConfigureLogging</c> on the
/// mediator's configuration (<c>AddMediator(cfg =&gt; cfg.ConfigureLogging(...))</c>).
/// </summary>
public sealed class LoggingPipelineOptions
{
    /// <summary>
    /// The level of the "Starting request" and "Completed request" lines; <see cref="LogLevel.Information"/> by default
    /// (as before). <see cref="LogLevel.Debug"/> keeps them out of a service's normal logs, which then show only failures,
    /// exceptions and slow requests.
    /// </summary>
    public LogLevel RequestLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// A request that takes longer is logged at <see cref="LogLevel.Warning"/> ("Slow request"); <see langword="null"/>
    /// (the default) logs none. What is slow depends on the service (one waiting on a remote API is slower than one
    /// reading its own database), so there is no threshold until the service sets one.
    /// </summary>
    public TimeSpan? SlowRequestThreshold { get; set; }
}
