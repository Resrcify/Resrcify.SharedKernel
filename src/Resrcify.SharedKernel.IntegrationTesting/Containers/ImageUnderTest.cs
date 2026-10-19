using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;

namespace Resrcify.SharedKernel.IntegrationTesting.Containers;

/// <summary>
/// Another service's image a test run runs, and exactly which build it was. Every container of the run runs the same
/// image: the first one pulls it (<see cref="ImageAcquisition"/>), and from then on the run names it by its registry
/// digest, so a tag moved mid-run can't mix two builds. <see cref="Description"/> says what it was (version, revision,
/// digest), and the first start writes it to the console as a <c>[tested against]</c> line, for the CI log.
/// </summary>
/// <remarks>
/// One instance per image per run (a static of the fixture, or a collection fixture's member): it remembers the digest
/// the first container resolved. Pin a build by naming it by digest (<c>repository@sha256:…</c>): it is then never
/// pulled when a local copy exists, and the line says "pinned by digest".
/// </remarks>
public sealed class ImageUnderTest
{
    private const string VersionLabel = "org.opencontainers.image.version";

    private const string RevisionLabel = "org.opencontainers.image.revision";

    private readonly ImageAcquisition _acquisition;

    private readonly string _overrides;

    private string _reference;

    private volatile string? _description;

    /// <param name="service">The service, as the log names it (e.g. <c>Orders</c>).</param>
    /// <param name="reference">The image as asked for (e.g. <c>ghcr.io/acme/orders:latest</c>, or by digest).</param>
    /// <param name="origin">Why this image: the default, or the setting that asked for it (for the log).</param>
    /// <param name="acquisition">Whether the run pulls it.</param>
    /// <param name="overrides">How to pick another image (e.g. "set ORDERS_IMAGE"), for a failed pull's message.</param>
    public ImageUnderTest(
        string service,
        string reference,
        string origin,
        ImageAcquisition acquisition,
        string overrides)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        Service = service;
        Requested = reference;
        Origin = origin;
        _reference = reference;
        _acquisition = acquisition;
        _overrides = overrides;
    }

    /// <summary>The service, as the log names it.</summary>
    public string Service { get; }

    /// <summary>The image as asked for.</summary>
    public string Requested { get; }

    /// <summary>Why this image: the default, or the setting that asked for it.</summary>
    public string Origin { get; }

    /// <summary>
    /// What the run runs, once its first container started: the image as asked for, how it was obtained, and what it
    /// is (its <c>org.opencontainers.image.version</c> / <c>.revision</c> labels, registry digest and image id).
    /// </summary>
    public string Description
        => _description ?? $"{Service}: {Requested} ({Origin}; not started yet)";

    /// <summary>
    /// Builds and starts a container of this image. <paramref name="configure"/> sets everything but the image and its
    /// pull policy. The run's first container pulls the image; a pull that fails throws, naming the image.
    /// </summary>
    /// <exception cref="InvalidOperationException">The first pull failed (<see cref="ImageAcquisition.PullFresh"/>).</exception>
    public async Task<IContainer> StartAsync(
        Func<ContainerBuilder, ContainerBuilder> configure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_description is not null)
            return await StartContainerAsync(configure, _reference, PullPolicy.Missing, cancellationToken);

        // The run's containers start one at a time (the tests of a collection run in turn). Were two first starts to
        // overlap, both would pull: no harm, the same release.
        var container = await StartContainerAsync(configure, _reference, FirstPullPolicy(), cancellationToken);
        await ResolveAsync(cancellationToken);
        return container;
    }

    private Func<ImageInspectResponse, bool> FirstPullPolicy()
        => _acquisition == ImageAcquisition.LocalOnly || ImageReferences.IsPinnedByDigest(_reference)
            ? PullPolicy.Missing
            : PullPolicy.Always;

    private async Task<IContainer> StartContainerAsync(
        Func<ContainerBuilder, ContainerBuilder> configure,
        string reference,
        Func<ImageInspectResponse, bool> pullPolicy,
        CancellationToken cancellationToken)
    {
        var container = configure(new ContainerBuilder(reference).WithImagePullPolicy(pullPolicy)).Build();
        try
        {
            await container.StartAsync(cancellationToken);
            return container;
        }
        catch (DockerApiException exception) when (_description is null && _acquisition == ImageAcquisition.PullFresh)
        {
            await container.DisposeAsync();
            throw new InvalidOperationException(PullFailed(reference, exception), exception);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    private string PullFailed(
        string reference,
        DockerApiException exception)
        => $"Could not pull {Service}'s image {reference} ({Origin}): {exception.Message.ReplaceLineEndings(" ").Trim()} " +
            "The run pulls it fresh and never falls back to a local copy, which could be an older release. " +
            $"Log in to its registry (docker login {ImageReferences.Registry(reference)}), or {_overrides}.";

    /// <summary>Reads what the run's first container ran, and names the image by its digest from then on.</summary>
    private async Task ResolveAsync(CancellationToken cancellationToken)
    {
        using var client = TestcontainersSettings.OS.DockerEndpointAuthConfig
            .GetDockerClientBuilder(ResourceReaper.DefaultSessionId)
            .Build();
        var image = await client.Images.InspectImageAsync(_reference, cancellationToken);
        var digest = RegistryDigest(image);
        if (digest is not null)
            _reference = digest;

        _description = Describe(image, digest);
        // Once per run, as the run's first container of this image starts: in the console of `dotnet test` (the CI
        // log), next to Testcontainers' own lines.
        Console.WriteLine($"[tested against] {_description}");
    }

    private string Describe(
        ImageInspectResponse image,
        string? digest)
    {
        var labels = image.Config?.Labels ?? new Dictionary<string, string>();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Service}: {Requested} ({Origin}; {ImageReferences.Obtained(Requested, _acquisition)}) = " +
            $"version {Label(labels, VersionLabel)}, revision {Label(labels, RevisionLabel)}, " +
            $"{digest ?? "no registry digest (a local image)"}, image {image.ID}, created {Created(image)}");
    }

    /// <summary>The image's digest in the repository it was asked for from (<c>repository@sha256:...</c>), if it has one.</summary>
    private string? RegistryDigest(ImageInspectResponse image)
    {
        var repository = ImageReferences.Repository(Requested);
        return image.RepoDigests?.FirstOrDefault(digest =>
            digest.StartsWith($"{repository}@", StringComparison.OrdinalIgnoreCase));
    }

    private static string Created(ImageInspectResponse image)
        => image.Created is { } created
            ? created.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            : "unknown";

    private static string Label(
        IDictionary<string, string> labels,
        string name)
        => labels.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "(unlabelled)";
}
