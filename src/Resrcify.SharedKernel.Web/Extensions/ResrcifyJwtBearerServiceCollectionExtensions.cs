using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Web.Authentication;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class ResrcifyJwtBearerServiceCollectionExtensions
{
    /// <summary>
    /// Authenticates requests with Resrcify.Identity's tokens, as the default scheme (JWT bearer): the
    /// <see cref="ResrcifyJwtOptions.SectionName"/> section (<c>Authority</c>, <c>Issuer</c>, <c>Audience</c>) is bound and
    /// validated at start-up, the signing keys come from the authority's discovery document, and the issuer,
    /// audience, lifetime and signature are checked. <paramref name="configure"/> runs after, for anything else
    /// (events, claim mapping). Authorization policies stay the service's own.
    /// </summary>
    /// <remarks>The service references <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> itself.</remarks>
    public static AuthenticationBuilder AddResrcifyJwtBearer(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<JwtBearerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<ResrcifyJwtOptions>()
            .Bind(configuration.GetSection(ResrcifyJwtOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                jwt => jwt.HasValidAuthority,
                $"{ResrcifyJwtOptions.SectionName}:{nameof(ResrcifyJwtOptions.Authority)} must be an absolute http(s) URI.")
            .ValidateOnStart();

        services.ConfigureOptions<ConfigureResrcifyJwtBearerOptions>();

        return services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(configure ?? (_ => { }));
    }
}
