using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.Web.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class OutboxAdministrationEndpointExtensionsTests : IAsyncLifetime
{
    private static readonly Guid MessageId = Guid.Parse("5b0f5c7e-58b5-4bd4-9a51-6d9f0e1f2a10");

    private static readonly OutboxMessageDetails GivenUp = new(
        MessageId,
        "Shards.Created",
        "{}",
        new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc),
        DateTime.MinValue,
        GaveUp: true,
        RetryCount: 3,
        NextAttemptOnUtc: null,
        Error: "Gave up.");

    private readonly IOutboxAdministration<AppDbContext> _outbox = Substitute.For<IOutboxAdministration<AppDbContext>>();

    private WebApplication _app = default!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_outbox);
        builder.Services.AddAuthentication(TestAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapOutboxAdministration<AppDbContext>();
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
        => await _app.DisposeAsync();

    [Fact]
    public async Task TheEndpoints_ShouldRequireAnAuthenticatedCaller()
    {
        using var response = await _app.GetTestClient().GetAsync(new Uri("/admin/outbox", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await _outbox.DidNotReceiveWithAnyArgs().GetSummaryAsync();
    }

    [Fact]
    public void TheEndpoints_ShouldBeNamedAfterTheContext()
    {
        var names = _app.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .ToList();

        names.ShouldBe(
        [
            "Outbox.AppDbContext.Summary",
            "Outbox.AppDbContext.GivenUp",
            "Outbox.AppDbContext.Message",
            "Outbox.AppDbContext.Retry",
            "Outbox.AppDbContext.RetryAll",
            "Outbox.AppDbContext.Delete",
            "Outbox.AppDbContext.DeleteAll",
        ],
        ignoreOrder: true);
    }

    [Fact]
    public async Task Summary_ShouldAnswerTheOutboxSummary()
    {
        _outbox.GetSummaryAsync(Arg.Any<CancellationToken>())
            .Returns(new OutboxSummary(2, 1, 1, null, [new OutboxEventTypeSummary("Shards.Created", "swgohapi", 2, 1, 1)]));

        var summary = await Admin().GetFromJsonAsync<OutboxSummary>(new Uri("/admin/outbox", UriKind.Relative));

        summary.ShouldNotBeNull().GivenUp.ShouldBe(1);
        summary.EventTypes.ShouldHaveSingleItem().Lane.ShouldBe("swgohapi");
    }

    [Fact]
    public async Task GivenUp_ShouldListFiftyByDefault_OfTheTypeWhenGiven()
    {
        _outbox.ListGivenUpAsync(50, "Shards.Created", Arg.Any<CancellationToken>())
            .Returns([new OutboxMessageSummary(MessageId, "Shards.Created", GivenUp.OccurredOnUtc, 3, "Gave up.")]);

        var listed = await Admin().GetFromJsonAsync<OutboxMessageSummary[]>(
            new Uri("/admin/outbox/given-up?type=Shards.Created", UriKind.Relative));

        listed.ShouldNotBeNull().ShouldHaveSingleItem().Id.ShouldBe(MessageId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task GivenUp_ShouldAnswer400_ForATakeOutOfRange(int take)
    {
        using var response = await Admin().GetAsync(new Uri($"/admin/outbox/given-up?take={take}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _outbox.DidNotReceiveWithAnyArgs().ListGivenUpAsync();
    }

    [Fact]
    public async Task Message_ShouldAnswerTheMessage_Or404ForAnUnknownOne()
    {
        _outbox.FindAsync(MessageId, Arg.Any<CancellationToken>()).Returns(GivenUp);

        var message = await Admin().GetFromJsonAsync<OutboxMessageDetails>(
            new Uri($"/admin/outbox/messages/{MessageId}", UriKind.Relative));
        using var unknown = await Admin().GetAsync(new Uri($"/admin/outbox/messages/{Guid.NewGuid()}", UriKind.Relative));

        message.ShouldBe(GivenUp);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Retry_ShouldAnswer204_Or404ForAMessageThatDidNotGiveUp()
    {
        _outbox.RetryAsync(MessageId, Arg.Any<CancellationToken>()).Returns(true);

        using var retried = await Admin().PostAsync(new Uri($"/admin/outbox/messages/{MessageId}/retry", UriKind.Relative), content: null);
        using var unknown = await Admin().PostAsync(new Uri($"/admin/outbox/messages/{Guid.NewGuid()}/retry", UriKind.Relative), content: null);

        retried.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RetryAll_ShouldAnswerHowManyWereTriedAgain()
    {
        _outbox.RetryAllGivenUpAsync("Shards.Created", Arg.Any<CancellationToken>()).Returns(2);

        using var response = await Admin().PostAsync(
            new Uri("/admin/outbox/given-up/retry?type=Shards.Created", UriKind.Relative),
            content: null);

        (await response.Content.ReadFromJsonAsync<OutboxRetried>()).ShouldBe(new OutboxRetried(2));
    }

    [Fact]
    public async Task Delete_ShouldAnswer204_Or404ForAMessageThatDidNotGiveUp()
    {
        _outbox.DeleteAsync(MessageId, Arg.Any<CancellationToken>()).Returns(true);

        using var deleted = await Admin().DeleteAsync(new Uri($"/admin/outbox/messages/{MessageId}", UriKind.Relative));
        using var unknown = await Admin().DeleteAsync(new Uri($"/admin/outbox/messages/{Guid.NewGuid()}", UriKind.Relative));

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteAll_ShouldAnswerHowManyWereDeleted()
    {
        _outbox.DeleteAllGivenUpAsync(null, Arg.Any<CancellationToken>()).Returns(4);

        using var response = await Admin().DeleteAsync(new Uri("/admin/outbox/given-up", UriKind.Relative));

        (await response.Content.ReadFromJsonAsync<OutboxDeleted>()).ShouldBe(new OutboxDeleted(4));
    }

    private HttpClient Admin()
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add(TestAuthentication.Header, "admin");
        return client;
    }

    /// <summary>Stands in for a service's DbContext: the endpoints only name it.</summary>
    internal sealed record AppDbContext(string ConnectionString);

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
