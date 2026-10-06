using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Resrcify.SharedKernel.Web.Authentication;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ResrcifyJwtBearerServiceCollectionExtensionsTests
    : IDisposable
{
    private const string Issuer = "Resrcify.Identity";
    private const string Audience = "Resrcify";

    private readonly RSA _identityKey = RSA.Create(2048);

    [Fact]
    public void AddResrcifyJwtBearer_ShouldPointTheBearerAtTheAuthority()
    {
        using var provider = Provider();

        var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        bearer.Authority.ShouldBe("http://identity:22000");
        bearer.RequireHttpsMetadata.ShouldBeFalse();
        bearer.TokenValidationParameters.ValidIssuer.ShouldBe(Issuer);
        bearer.TokenValidationParameters.ValidAudience.ShouldBe(Audience);
        bearer.TokenValidationParameters.ValidateIssuer.ShouldBeTrue();
        bearer.TokenValidationParameters.ValidateAudience.ShouldBeTrue();
        bearer.TokenValidationParameters.ValidateLifetime.ShouldBeTrue();
        bearer.TokenValidationParameters.ValidateIssuerSigningKey.ShouldBeTrue();
    }

    [Fact]
    public async Task AddResrcifyJwtBearer_ShouldMakeTheBearerTheDefaultScheme()
    {
        using var provider = Provider();

        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync();

        scheme.ShouldNotBeNull().Name.ShouldBe(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void AddResrcifyJwtBearer_ShouldRequireHttpsMetadata_WhenConfigured()
    {
        using var provider = Provider(Settings(authority: "https://identity.resrcify.com", requireHttps: "true"));

        provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .RequireHttpsMetadata
            .ShouldBeTrue();
    }

    [Fact]
    public void AddResrcifyJwtBearer_ShouldLetTheServiceChangeTheBearer_AfterItsOwnSettings()
    {
        using var provider = Provider(configure: bearer => bearer.TokenValidationParameters.ValidAudience = "Other");

        provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters.ValidAudience
            .ShouldBe("Other");
    }

    [Theory]
    [InlineData(null, Issuer, Audience)]
    [InlineData("identity:22000", Issuer, Audience)]
    [InlineData("http://identity:22000", null, Audience)]
    [InlineData("http://identity:22000", Issuer, null)]
    public void AddResrcifyJwtBearer_ShouldFailValidation_WhenTheSectionIsIncomplete(
        string? authority,
        string? issuer,
        string? audience)
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["Jwt:Authority"] = authority,
            ["Jwt:Issuer"] = issuer,
            ["Jwt:Audience"] = audience,
        });

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ResrcifyJwtOptions>>().Value);
    }

    [Fact]
    public async Task AddResrcifyJwtBearer_ShouldAcceptIdentitysToken()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, Token(Issuer, Audience, _identityKey));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("player-1");
    }

    [Fact]
    public async Task AddResrcifyJwtBearer_ShouldRejectAToken_ForAnotherAudience()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, Token(Issuer, "SomeoneElse", _identityKey));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddResrcifyJwtBearer_ShouldRejectAToken_FromAnotherIssuer()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, Token("Impostor", Audience, _identityKey));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddResrcifyJwtBearer_ShouldRejectAToken_SignedWithAnotherKey()
    {
        await using var app = await StartAsync();
        using var otherKey = RSA.Create(2048);

        using var response = await GetAsync(app, Token(Issuer, Audience, otherKey));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    public void Dispose()
        => _identityKey.Dispose();

    private static ServiceProvider Provider(
        Dictionary<string, string?>? settings = null,
        Action<JwtBearerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResrcifyJwtBearer(Configuration(settings ?? Settings()), configure);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> Settings(
        string authority = "http://identity:22000",
        string? requireHttps = null)
        => new()
        {
            ["Jwt:Authority"] = authority,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:RequireHttpsMetadata"] = requireHttps,
        };

    private static IConfiguration Configuration(
        Dictionary<string, string?> settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

    // Identity's discovery document, given directly instead of fetched from the authority.
    private Task<WebApplication> StartAsync()
        => TestHosts.StartAsync(
            services =>
            {
                services.AddResrcifyJwtBearer(
                    Configuration(Settings()),
                    bearer =>
                    {
                        var discovery = new OpenIdConnectConfiguration { Issuer = Issuer };
                        discovery.SigningKeys.Add(new RsaSecurityKey(_identityKey.ExportParameters(false)) { KeyId = "identity" });
                        bearer.Configuration = discovery;
                    });
                services.AddAuthorization();
            },
            app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapGet("/me", (HttpContext context) => context.User.FindFirst("player")?.Value)
                    .RequireAuthorization();
            });

    private static string Token(
        string issuer,
        string audience,
        RSA key)
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = new Dictionary<string, object> { ["player"] = "player-1" },
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(key) { KeyId = "identity" },
                SecurityAlgorithms.RsaSha256),
        });

    private static async Task<HttpResponseMessage> GetAsync(
        WebApplication app,
        string token)
    {
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/me", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
