using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
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

        // A delegate, not a type implementing IConfigureNamedOptions<JwtBearerOptions>: MVC loads every type of this
        // assembly to find controllers (ApiController), and such a type would need the JwtBearer package in every
        // controller host, also those that never call this method.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<ResrcifyJwtOptions>>(ApplyResrcifyJwt);

        return services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(configure ?? (_ => { }));
    }

    /// <summary>
    /// Points the JWT bearer scheme at Resrcify.Identity (<see cref="ResrcifyJwtOptions"/>): signing keys from the
    /// authority's discovery document (JWKS), and the issuer, audience, lifetime and signature all checked.
    /// </summary>
    private static void ApplyResrcifyJwt(
        JwtBearerOptions options,
        IOptions<ResrcifyJwtOptions> jwtOptions)
    {
        var jwt = jwtOptions.Value;

        options.Authority = jwt.Authority;
        options.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
        };
    }
}
