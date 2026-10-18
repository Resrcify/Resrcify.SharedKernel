using Microsoft.AspNetCore.Builder;

namespace Resrcify.SharedKernel.Observability.Logging;

/// <summary>
/// Records in <see cref="IApplicationBuilder.Properties"/> that a pipeline has the request logging, and which call added
/// it, so a second one (which would log every request twice) is refused when the pipeline is built, with a message naming
/// both calls.
/// </summary>
/// <remarks>
/// The mark is the pipeline's own: a branch (<c>UseWhen</c>, <c>Map</c>) built after it inherits it, as its requests
/// already pass the request logging; one built before it doesn't.
/// </remarks>
internal static class RequestLoggingMark
{
    public const string Key = "Resrcify.SharedKernel.Observability.RequestLogging";

    public const string ByServiceDefaults = "UseServiceDefaults";

    public const string ByServiceRequestLogging = "UseServiceRequestLogging";

    /// <summary>The call that added the request logging to <paramref name="app"/>, or <see langword="null"/>.</summary>
    public static string? AddedBy(
        IApplicationBuilder app)
        => app.Properties.TryGetValue(Key, out var addedBy)
            ? addedBy as string
            : null;

    public static void Set(
        IApplicationBuilder app,
        string addedBy)
        => app.Properties[Key] = addedBy;
}
