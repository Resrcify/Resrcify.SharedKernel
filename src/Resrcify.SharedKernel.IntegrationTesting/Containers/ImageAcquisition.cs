using DotNet.Testcontainers.Images;

namespace Resrcify.SharedKernel.IntegrationTesting.Containers;

/// <summary>How a run gets the image of a service it runs (<see cref="ImageUnderTest"/>).</summary>
public enum ImageAcquisition
{
    /// <summary>
    /// Pulled fresh from its registry by the run's first container (Testcontainers' <see cref="PullPolicy.Always"/>):
    /// a stale local copy of a moving tag such as <c>latest</c> never stands in for a newer release, and a failed pull
    /// fails the run. An image named by its digest is immutable, so a local copy of it is used.
    /// </summary>
    PullFresh,

    /// <summary>
    /// The local copy as it is (an offline run, or an image built on this machine), not pulled fresh: pulled only when
    /// there is no local copy (Testcontainers' <see cref="PullPolicy.Missing"/>).
    /// </summary>
    LocalOnly,
}
