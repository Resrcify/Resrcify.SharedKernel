namespace Resrcify.SharedKernel.Web.Health;

/// <summary>The paths <c>MapHealthEndpoints()</c> maps.</summary>
public sealed class HealthEndpointOptions
{
    /// <summary>Every check.</summary>
    public string Path { get; set; } = "/health";

    /// <summary>The checks tagged <see cref="HealthTags.Ready"/>: the readiness probe.</summary>
    public string ReadyPath { get; set; } = "/health/ready";

    /// <summary>The checks tagged <see cref="HealthTags.Live"/>: the liveness probe.</summary>
    public string LivePath { get; set; } = "/health/live";
}
