using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Resrcify.SharedKernel.Web.Authentication;

/// <summary>
/// Points the JWT bearer scheme at Resrcify.Identity (<see cref="ResrcifyJwtOptions"/>): signing keys from the
/// authority's discovery document (JWKS), and the issuer, audience, lifetime and signature all checked.
/// </summary>
internal sealed class ConfigureResrcifyJwtBearerOptions(
    IOptions<ResrcifyJwtOptions> jwtOptions)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(
        string? name,
        JwtBearerOptions options)
    {
        if (string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
            Configure(options);
    }

    public void Configure(
        JwtBearerOptions options)
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
