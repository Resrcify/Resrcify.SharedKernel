using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Web.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.Web.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxAdministrationEndpointExtensionsTests : IAsyncLifetime
{
    private WebApplication _app = default!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        // One in-memory database for the test, kept open (and disposed) by the container.
        builder.Services.AddSingleton(_ =>
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            return connection;
        });
        builder.Services.AddDbContext<AppDbContext>((provider, options) => options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
        builder.Services.AddScoped<OutboxAdministration<AppDbContext>>();
        builder.Services.AddAuthentication(TestAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapOutboxAdministration<AppDbContext>();
        await _app.StartAsync();

        await using var scope = _app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
        => await _app.DisposeAsync();

    [Fact]
    public async Task TheEndpoints_ShouldRequireAnAuthenticatedCaller()
    {
        using var response = await _app.GetTestClient().GetAsync(new Uri("/admin/outbox", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Summary_ShouldCountTheMessagesThatGaveUp()
    {
        await SeedGivenUpAsync();

        var summary = await Admin().GetFromJsonAsync<OutboxSummary>(new Uri("/admin/outbox", UriKind.Relative));

        summary.ShouldNotBeNull().GivenUp.ShouldBe(1);
    }

    [Fact]
    public async Task Retry_ShouldMakeAMessageThatGaveUpWaitAgain_And404ForOneThatDidNot()
    {
        var id = await SeedGivenUpAsync();
        var retry = new Uri($"/admin/outbox/messages/{id}/retry", UriKind.Relative);

        using var retried = await Admin().PostAsync(retry, content: null);
        using var again = await Admin().PostAsync(retry, content: null);

        retried.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var message = await Admin().GetFromJsonAsync<OutboxMessageDetails>(new Uri($"/admin/outbox/messages/{id}", UriKind.Relative));
        message.ShouldNotBeNull().GaveUp.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenUp_ShouldRefuseATakeOutOfRange()
    {
        using var response = await Admin().GetAsync(new Uri("/admin/outbox/given-up?take=0", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private HttpClient Admin()
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add(TestAuthentication.Header, "admin");
        return client;
    }

    private async Task<Guid> SeedGivenUpAsync()
    {
        var id = Guid.NewGuid();
        await using var scope = _app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = id,
            Type = "Shards.Created",
            Content = "{}",
            OccurredOnUtc = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc),
            ProcessedOnUtc = OutboxMessage.GivenUpProcessedOnUtc,
            RetryCount = 3,
            Error = "Gave up.",
        });
        await context.SaveChangesAsync();
        return id;
    }

    internal sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplyOutboxMessageConfiguration();
    }

    /// <summary>Authenticates a request carrying the test header.</summary>
    internal sealed class TestAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string Header = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers[Header] is not [{ } user])
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
