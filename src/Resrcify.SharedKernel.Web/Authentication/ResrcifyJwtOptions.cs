using System;
using System.ComponentModel.DataAnnotations;

namespace Resrcify.SharedKernel.Web.Authentication;

/// <summary>
/// The <c>Jwt</c> configuration section of a service accepting Resrcify.Identity's tokens. Identity signs with RSA
/// (RS256) and publishes its public keys through OpenID Connect discovery at <see cref="Authority"/>, so a service
/// holds no secret: the keys are fetched from there, and refreshed when Identity rotates them.
/// </summary>
public sealed class ResrcifyJwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Identity's base URL, as this service reaches it (e.g. <c>http://identity:22000</c>).</summary>
    [Required]
    public string Authority { get; set; } = string.Empty;

    /// <summary>The issuer a token must carry (<c>iss</c>), e.g. <c>Resrcify.Identity</c>.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The audience a token must carry (<c>aud</c>), e.g. <c>Resrcify</c>.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Whether the discovery document and keys must come over HTTPS. Off by default: the services reach Identity
    /// inside the cluster over plain HTTP. Turn it on when the authority is reached over a network you don't trust.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; }

    /// <summary><see cref="Authority"/> is an absolute http(s) URI.</summary>
    internal bool HasValidAuthority
        => Uri.TryCreate(Authority, UriKind.Absolute, out var authority)
            && (authority.Scheme == Uri.UriSchemeHttp || authority.Scheme == Uri.UriSchemeHttps);
}
