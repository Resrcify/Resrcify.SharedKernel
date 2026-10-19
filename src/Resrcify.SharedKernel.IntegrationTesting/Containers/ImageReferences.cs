using System;

namespace Resrcify.SharedKernel.IntegrationTesting.Containers;

/// <summary>Reads an image reference (<c>[registry/]repository[:tag][@digest]</c>) for <see cref="ImageUnderTest"/>.</summary>
internal static class ImageReferences
{
    private const string DockerHub = "docker.io";

    /// <summary>Whether the reference names one build by its digest (<c>…@sha256:…</c>).</summary>
    public static bool IsPinnedByDigest(string reference)
        => reference.Contains('@', StringComparison.Ordinal);

    /// <summary>The reference without its tag or digest: <c>ghcr.io/acme/orders</c>.</summary>
    public static string Repository(string reference)
    {
        var at = reference.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0)
            return reference[..at];

        // A colon before the last slash is a registry's port (localhost:5000/orders), not a tag.
        var lastSlash = reference.LastIndexOf('/');
        var tag = reference.LastIndexOf(':');
        return tag > lastSlash ? reference[..tag] : reference;
    }

    /// <summary>
    /// The registry the reference pulls from: its first part when that names a host (a dot, a port or
    /// <c>localhost</c>), Docker Hub otherwise.
    /// </summary>
    public static string Registry(string reference)
    {
        var slash = reference.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0)
            return DockerHub;

        var first = reference[..slash];
        var namesAHost = first.Contains('.', StringComparison.Ordinal)
            || first.Contains(':', StringComparison.Ordinal)
            || string.Equals(first, "localhost", StringComparison.Ordinal);
        return namesAHost ? first : DockerHub;
    }

    /// <summary>How the run got the image: a digest names exactly one build, a tag is pulled fresh or run as it is.</summary>
    public static string Obtained(
        string reference,
        ImageAcquisition acquisition)
    {
        if (IsPinnedByDigest(reference))
            return "pinned by digest: exactly this build";

        return acquisition == ImageAcquisition.PullFresh
            ? "pulled fresh"
            : "local copy, not pulled fresh";
    }
}
