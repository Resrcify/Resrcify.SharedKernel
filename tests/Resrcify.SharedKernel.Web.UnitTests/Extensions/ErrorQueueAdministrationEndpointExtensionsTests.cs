using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ErrorQueueAdministrationEndpointExtensionsTests : IAsyncLifetime
{
    private static readonly FailedMessage Failed = new(
        "message-1",
        "ShardRankChanged",
        "discordmanagement",
        "RankChangedHandler failed 5 times: Discord.Down (Failure): Down.",
        DateTimeOffset.UnixEpoch,
        new Dictionary<string, string> { ["rbs2-msg-id"] = "message-1" },
        """{"shardId":"s1"}""",
        "{}"u8.ToArray());

    private readonly IErrorQueue _errorQueue = Substitute.For<IErrorQueue>();

    private WebApplication _app = default!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_errorQueue);
        builder.Services.AddAuthentication(TestAuthentication.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapErrorQueueAdministration();
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
        => await _app.DisposeAsync();

    [Fact]
    public async Task TheEndpoints_ShouldRequireAnAuthenticatedCaller()
    {
        using var response = await _app.GetTestClient().GetAsync(new Uri("/admin/error-queue", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await _errorQueue.DidNotReceiveWithAnyArgs().ListAsync();
    }

    [Fact]
    public async Task List_ShouldAnswerTheMessagesAndTheirCount()
    {
        _errorQueue.ListAsync(10, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ErrorQueueContents("discordmanagement.error", 3, [Failed])));

        var listed = await Admin().GetFromJsonAsync<ErrorQueueContents>(new Uri("/admin/error-queue?take=10", UriKind.Relative));

        listed.ShouldNotBeNull().Count.ShouldBe(3);
        var message = listed.Messages.ShouldHaveSingleItem();
        message.MessageId.ShouldBe("message-1");
        message.Error.ShouldBe(Failed.Error);
        message.Text.ShouldBe(Failed.Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task List_ShouldAnswer400_ForATakeOutOfRange(int take)
    {
        using var response = await Admin().GetAsync(new Uri($"/admin/error-queue?take={take}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_ShouldAnswer404_WhenTheServiceHasNoErrorQueue()
    {
        _errorQueue.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ErrorQueueContents>(new Error("ErrorQueue.None", "No error queue.", ErrorType.NotFound)));

        using var response = await Admin().GetAsync(new Uri("/admin/error-queue", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_ShouldAnswer204_Or404ForAnUnknownMessage()
    {
        _errorQueue.DeleteAsync("message-1", Arg.Any<CancellationToken>()).Returns(Result.Success());
        _errorQueue.DeleteAsync("unknown", Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new Error("ErrorQueue.MessageNotFound", "No message unknown.", ErrorType.NotFound)));

        using var deleted = await Admin().DeleteAsync(new Uri("/admin/error-queue/messages/message-1", UriKind.Relative));
        using var unknown = await Admin().DeleteAsync(new Uri("/admin/error-queue/messages/unknown", UriKind.Relative));

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteAll_ShouldAnswerHowManyWereDeleted_OfTheType()
    {
        _errorQueue.DeleteAllAsync("ShardRankChanged", Arg.Any<CancellationToken>()).Returns(Result.Success(4));

        using var response = await Admin().DeleteAsync(new Uri("/admin/error-queue?type=ShardRankChanged", UriKind.Relative));

        (await response.Content.ReadFromJsonAsync<ErrorQueueDeleted>()).ShouldBe(new ErrorQueueDeleted(4));
    }

    [Fact]
    public async Task Retry_ShouldAnswer204_Or409WhenTheMessageCantBeMovedBack()
    {
        _errorQueue.RetryAsync("message-1", Arg.Any<CancellationToken>()).Returns(Result.Success());
        _errorQueue.RetryAsync("stuck", Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new Error("ErrorQueue.NotMoved", "Its queue is gone.", ErrorType.Conflict)));

        using var retried = await Admin().PostAsync(new Uri("/admin/error-queue/messages/message-1/retry", UriKind.Relative), null);
        using var stuck = await Admin().PostAsync(new Uri("/admin/error-queue/messages/stuck/retry", UriKind.Relative), null);

        retried.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        stuck.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RetryAll_ShouldAnswerHowManyWereMovedBack()
    {
        _errorQueue.RetryAllAsync(null, Arg.Any<CancellationToken>()).Returns(Result.Success(7));

        using var response = await Admin().PostAsync(new Uri("/admin/error-queue/retry", UriKind.Relative), null);

        (await response.Content.ReadFromJsonAsync<ErrorQueueRetried>()).ShouldBe(new ErrorQueueRetried(7));
    }

    private HttpClient Admin()
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add(TestAuthentication.Header, "admin");
        return client;
    }

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
