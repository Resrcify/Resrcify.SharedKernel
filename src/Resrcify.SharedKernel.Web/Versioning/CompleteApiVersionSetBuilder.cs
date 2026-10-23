using System;
using System.Collections.Frozen;
using System.Globalization;
using Asp.Versioning;
using Asp.Versioning.Builder;

namespace Resrcify.SharedKernel.Web.Versioning;

/// <summary>
/// Builds a version set holding <c>versions</c>, reported, that no endpoint changes afterwards
/// (<see cref="CompleteApiVersionSet"/>).
/// </summary>
internal sealed class CompleteApiVersionSetBuilder : ApiVersionSetBuilder
{
    private readonly FrozenSet<ApiVersion> _versions;

    public CompleteApiVersionSetBuilder(params ApiVersion[] versions)
        : base(name: null)
    {
        _versions = versions.ToFrozenSet();
        foreach (var version in _versions)
            HasApiVersion(version);

        ReportApiVersions();
    }

    public override ApiVersionSet Build()
        => new CompleteApiVersionSet(this, _versions);

    /// <summary>
    /// A version set every version is declared in from the start. An endpoint's <c>HasApiVersion</c> advertises its
    /// version in the endpoint's set as the endpoint is built; Asp.Versioning's set adds it to a collection then (which
    /// changes nothing the set reports for a version it has already: its supported versions are its declared ones and
    /// its advertised ones). This one writes nothing, so hosts building their endpoints at the same time can share it.
    /// </summary>
    private sealed class CompleteApiVersionSet(
        ApiVersionSetBuilder builder,
        FrozenSet<ApiVersion> versions)
        : ApiVersionSet(builder, name: null)
    {
        public override void AdvertisesApiVersion(ApiVersion apiVersion)
        {
            if (!versions.Contains(apiVersion))
                throw OutsideTheSet(apiVersion, "has");
        }

        public override void AdvertisesDeprecatedApiVersion(ApiVersion apiVersion)
            => throw OutsideTheSet(apiVersion, "deprecates");

        private InvalidOperationException OutsideTheSet(
            ApiVersion apiVersion,
            string verb)
            => new(string.Format(
                CultureInfo.InvariantCulture,
                "An endpoint {0} API version {1}, which ApiVersioning.VersionSet doesn't hold (it holds {2}, and no " +
                "endpoint changes it). Give that endpoint a version set of its own (app.NewApiVersionSet()).",
                verb,
                apiVersion,
                string.Join(", ", versions)));
    }
}
