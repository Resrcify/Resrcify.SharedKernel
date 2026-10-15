namespace Resrcify.SharedKernel.Abstractions.Health;

/// <summary>
/// The health check tags the Web package's <c>MapHealthEndpoints()</c> selects by, shared by every package that
/// registers a check: SharedKernel's checks take <see cref="Ready"/> (the outbox, the message bus, PostgreSQL) or
/// <see cref="Live"/> (the Quartz jobs) when the service gives no tags, and a service tags its own checks with them,
/// e.g. <c>AddCheck&lt;UpstreamHealthCheck&gt;("upstream", tags: [HealthTags.Ready])</c>.
/// </summary>
public static class HealthTags
{
    /// <summary>Checks <c>/health/ready</c> runs: what the service needs to take traffic (its database, its broker).</summary>
    public const string Ready = "ready";

    /// <summary>
    /// Checks <c>/health/live</c> runs: what only a restart fixes (a stuck job loop). None: the endpoint answers
    /// Healthy as long as the process serves requests.
    /// </summary>
    public const string Live = "live";
}
