using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace Resrcify.SharedKernel.Observability.Configuration;

/// <summary>
/// The requests left out of the traces and the request log: <see cref="ServiceTelemetryOptions.UntracedPaths"/> as
/// <c>AddServiceTelemetry</c> was given them (the probes and the scrape by default), registered for
/// <c>UseServiceRequestLogging</c>.
/// </summary>
internal sealed class UntracedRequests(
    IEnumerable<string> paths)
{
    private readonly string[] _paths = [.. paths];

    /// <summary>Whether <paramref name="path"/> is one of the paths, or under one (whole segments, ignoring case).</summary>
    public bool Contains(
        PathString path)
        => _paths.Any(untraced => path.StartsWithSegments(untraced, StringComparison.OrdinalIgnoreCase));
}
