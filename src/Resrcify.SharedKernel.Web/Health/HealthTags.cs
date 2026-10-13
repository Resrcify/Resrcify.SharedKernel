namespace Resrcify.SharedKernel.Web.Health;

/// <summary>
/// The health check tags <c>MapHealthEndpoints()</c> selects by. Tag a check when registering it, e.g.
/// <c>AddNpgSql(..., tags: [HealthTags.Ready])</c>.
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
