using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Endpoints;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class RequestEndpointExtensionsTests
{
    private static readonly Guid KnownShard = Guid.Parse("2b8f4a8e-1d4e-4a59-9a7e-0f3c2d1b6a10");
    private static readonly ShardView Main = new(KnownShard, "Main");

    private readonly ISender _sender = Substitute.For<ISender>();

    [Fact]
    public async Task ACommand_ShouldBeBuiltFromRouteValuesAndTheBody_AndAnswer204()
    {
        object? sent = null;
        _sender.Send(Arg.Do<IRequest<Result>>(request => sent = request), Arg.Any<CancellationToken>()).Returns(Result.Success());
        await using var app = await StartAsync(endpoints => endpoints.MapPostRequest(
            "/shards/{shardId:guid}/members/{allyCode:long}",
            (Guid shardId, long allyCode, MemberBody body) => new AddMember(shardId, allyCode, body.Emoji)));

        using var response = await app.GetTestClient().PostAsJsonAsync($"/shards/{KnownShard}/members/123456789", new MemberBody(":star:"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        sent.ShouldBe(new AddMember(KnownShard, 123456789, ":star:"));
    }

    [Fact]
    public async Task ACommandWithAResult_ShouldAnswer200WithIt()
    {
        _sender.Send(Arg.Any<IRequest<Result<ShardView>>>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Main));
        await using var app = await StartAsync(endpoints => endpoints.MapPostRequest("/shards", (NameBody body) => new CreateShard(body.Name)));

        using var response = await app.GetTestClient().PostAsJsonAsync("/shards", new NameBody("Main"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ShardView>()).ShouldBe(Main);
    }

    [Fact]
    public async Task AQuery_ShouldAnswer200_ForGetAndForPost()
    {
        _sender.Send(Arg.Any<IRequest<Result<ShardView>>>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Main));
        await using var app = await StartAsync(endpoints =>
        {
            endpoints.MapGetRequest("/shards/{shardId:guid}", (Guid shardId) => new GetShard(shardId));
            endpoints.MapPostRequest("/shards/search", (NameBody body) => new FindShard(body.Name));
        });

        var got = await app.GetTestClient().GetFromJsonAsync<ShardView>($"/shards/{KnownShard}");
        using var searched = await app.GetTestClient().PostAsJsonAsync("/shards/search", new NameBody("Main"));

        got.ShouldBe(Main);
        (await searched.Content.ReadFromJsonAsync<ShardView>()).ShouldBe(Main);
    }

    [Fact]
    public async Task EveryKindOfRequest_ShouldBeSent()
    {
        _sender.Send(Arg.Any<IRequest<Result<ShardView>>>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Main));
        _sender.Send(Arg.Any<IRequest<Result>>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        await using var app = await StartAsync(endpoints =>
        {
            endpoints.MapGetRequest("/cached", () => new CachedShard());
            endpoints.MapPostRequest("/transaction", () => new RenameInTransaction());
            endpoints.MapPostRequest("/transaction-with-result", () => new CreateInTransaction());
            endpoints.MapGetRequest("/own", () => new OwnRequest());
        });
        var client = app.GetTestClient();

        (await client.GetFromJsonAsync<ShardView>("/cached")).ShouldBe(Main);
        using var renamed = await client.PostAsync(new Uri("/transaction", UriKind.Relative), content: null);
        using var created = await client.PostAsync(new Uri("/transaction-with-result", UriKind.Relative), content: null);
        (await client.GetFromJsonAsync<ShardView>("/own")).ShouldBe(Main);

        renamed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        created.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnAsyncDelegate_WithServices_ShouldBeAwaited()
    {
        _sender.Send(Arg.Any<IRequest<Result<ShardView>>>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Main));
        await using var app = await StartAsync(
            endpoints => endpoints.MapGetRequest(
                "/shards/main",
                async (ShardNames names, CancellationToken cancellationToken) => new GetShard(await names.IdOfAsync("Main", cancellationToken))),
            services => services.AddSingleton<ShardNames>());

        (await app.GetTestClient().GetFromJsonAsync<ShardView>("/shards/main")).ShouldBe(Main);
        await _sender.Received(1).Send(Arg.Is<IRequest<Result<ShardView>>>(query => query.Equals(new GetShard(KnownShard))), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailure_ShouldBeProblemDetails_UnlessTheEndpointOrTheOptionsAnswerOtherwise()
    {
        var notFound = new Error("Shard.NotFound", "No such shard", ErrorType.NotFound);
        _sender.Send(Arg.Any<IRequest<Result<ShardView>>>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<ShardView>(notFound));
        await using var app = await StartAsync(endpoints =>
        {
            endpoints.MapGetRequest("/default", () => new GetShard(KnownShard));
            endpoints.MapGetRequest("/own", () => new GetShard(KnownShard), onFailure: _ => TypedResults.StatusCode(StatusCodes.Status418ImATeapot));
        });
        await using var configured = await StartAsync(
            endpoints => endpoints.MapGetRequest("/configured", () => new GetShard(KnownShard)),
            services => services.Configure<RequestEndpointOptions>(options => options.OnFailure = _ => TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable)));

        using var byDefault = await app.GetTestClient().GetAsync(new Uri("/default", UriKind.Relative));
        using var byEndpoint = await app.GetTestClient().GetAsync(new Uri("/own", UriKind.Relative));
        using var byOptions = await configured.GetTestClient().GetAsync(new Uri("/configured", UriKind.Relative));

        byDefault.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await byDefault.Content.ReadAsStringAsync()).ShouldContain("Shard.NotFound");
        ((int)byEndpoint.StatusCode).ShouldBe(StatusCodes.Status418ImATeapot);
        byOptions.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task OnSuccess_ShouldShapeTheAnswer_AndDescribeItForOpenApi()
    {
        _sender.Send(Arg.Any<IRequest<Result<ShardView>>>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Main));
        _sender.Send(Arg.Any<IRequest<Result>>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        await using var app = await StartAsync(endpoints =>
        {
            endpoints.MapPostRequest("/shards", (NameBody body) => new CreateShard(body.Name),
                    onSuccess: (ShardView shard) => TypedResults.Created($"/shards/{shard.Id}", shard))
                .WithName("Created");
            endpoints.MapPostRequest("/jobs", () => new RenameInTransaction(), onSuccess: () => TypedResults.Accepted(new Uri("/jobs/1", UriKind.Relative)))
                .WithName("Accepted");
            endpoints.MapGetRequest("/names/{shardId:guid}", (Guid shardId) => new GetShard(shardId),
                onSuccess: (ShardView shard) => TypedResults.Ok(shard.Name));
        });
        var client = app.GetTestClient();

        using var created = await client.PostAsJsonAsync("/shards", new NameBody("Main"));
        using var accepted = await client.PostAsync(new Uri("/jobs", UriKind.Relative), content: null);
        var name = await client.GetFromJsonAsync<string>($"/names/{KnownShard}");

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.Location.ShouldBe(new Uri($"/shards/{KnownShard}", UriKind.Relative));
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        name.ShouldBe("Main");
        StatusCodesOf(app, "Created").ShouldBe([201, 400, 404, 409, 500], ignoreOrder: true);
        StatusCodesOf(app, "Accepted").ShouldBe([202, 400, 404, 409, 500], ignoreOrder: true);
    }

    [Fact]
    public async Task OpenApi_ShouldDescribeTheResponse_AndNeverTheRequest()
    {
        await using var app = await StartAsync(endpoints =>
        {
            endpoints.MapGetRequest("/shards/{shardId:guid}", (Guid shardId) => new GetShard(shardId)).WithName("Query");
            endpoints.MapPostRequest("/members", (MemberBody body) => new AddMember(KnownShard, 1, body.Emoji)).WithName("Command");
        });

        var query = Produces(app, "Query");
        var command = Produces(app, "Command");

        query.Single(produces => produces.StatusCode == 200).Type.ShouldBe(typeof(ShardView));
        query.ShouldNotContain(produces => produces.Type == typeof(GetShard));
        command.Select(produces => produces.StatusCode).ShouldBe([204, 400, 404, 409, 500], ignoreOrder: true);
        command.ShouldNotContain(produces => produces.Type == typeof(AddMember));
    }

    [Fact]
    public async Task AFilterOnTheEndpoint_ShouldBeAbleToAnswerInstead()
    {
        await using var app = await StartAsync(endpoints => endpoints
            .MapPostRequest("/shards", (NameBody body) => new CreateShard(body.Name))
            .AddEndpointFilter((_, _) => ValueTask.FromResult<object?>(TypedResults.ValidationProblem(new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["name"] = ["Name is required."],
            }))));

        using var response = await app.GetTestClient().PostAsJsonAsync("/shards", new NameBody(""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _sender.DidNotReceiveWithAnyArgs().Send(Arg.Any<IRequest<Result<ShardView>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AReplayedRequest_ShouldBeMarkedReplayed()
    {
        _sender.Send(Arg.Any<IRequest<Result<ShardView>>>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Main));
        var idempotency = Substitute.For<IIdempotencyContext>();
        idempotency.WasReplayed(Arg.Is<object>(request => request.Equals(new CreateShard("Replayed")))).Returns(true);
        await using var app = await StartAsync(
            endpoints => endpoints.MapPostRequest("/shards", (NameBody body) => new CreateShard(body.Name)),
            services => services.AddSingleton(idempotency));

        using var replayed = await app.GetTestClient().PostAsJsonAsync("/shards", new NameBody("Replayed"));
        using var handled = await app.GetTestClient().PostAsJsonAsync("/shards", new NameBody("New"));

        replayed.Headers.GetValues(IdempotencyHeaders.Replayed).ShouldBe(["true"]);
        handled.Headers.Contains(IdempotencyHeaders.Replayed).ShouldBeFalse();
    }

    [Fact]
    public async Task ADelegateNotReturningARequest_ShouldThrowWhenMapped()
    {
        var failure = await Should.ThrowAsync<ArgumentException>(() => StartAsync(
            endpoints => endpoints.MapGetRequest("/shards", () => new ShardView(KnownShard, "Main"))));

        failure.Message.ShouldContain("isn't a request answered with a Result");
    }

    [Fact]
    public async Task OnSuccess_ShouldThrowWhenMapped_WhenItTakesTheWrongValue()
    {
        var failure = await Should.ThrowAsync<ArgumentException>(() => StartAsync(
            endpoints => endpoints.MapGetRequest("/shards", () => new GetShard(KnownShard), onSuccess: (string name) => TypedResults.Ok(name))));

        failure.Message.ShouldContain("onSuccess must take the request's ShardView");
    }

    private static IProducesResponseTypeMetadata[] Produces(WebApplication app, string name)
        => [.. app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name)
            .Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()];

    private static int[] StatusCodesOf(WebApplication app, string name)
        => [.. Produces(app, name).Select(produces => produces.StatusCode)];

    private async Task<WebApplication> StartAsync(Action<IEndpointRouteBuilder> map, Action<IServiceCollection>? services = null)
        => await TestHosts.StartAsync(
            collection =>
            {
                collection.AddSingleton(_sender);
                services?.Invoke(collection);
            },
            app => map(app));

    internal sealed record MemberBody(string Emoji);

    internal sealed record NameBody(string Name);

    internal sealed record ShardView(Guid Id, string Name);

    internal sealed record AddMember(Guid ShardId, long AllyCode, string Emoji) : ICommand;

    internal sealed record CreateShard(string Name) : ICommand<ShardView>;

    internal sealed record GetShard(Guid ShardId) : IQuery<ShardView>;

    internal sealed record FindShard(string Name) : IQuery<ShardView>;

    internal sealed record CachedShard : ICachingQuery<ShardView>
    {
        public string? CacheKey { get; set; } = "shard";

        public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(1);
    }

    internal sealed record RenameInTransaction : ITransactionCommand
    {
        public TimeSpan? CommandTimeout => null;

        public IsolationLevel? IsolationLevel => null;
    }

    internal sealed record CreateInTransaction : ITransactionCommand<ShardView>
    {
        public TimeSpan? CommandTimeout => null;

        public IsolationLevel? IsolationLevel => null;
    }

    /// <summary>A request of the service's own kind, neither a command nor a query.</summary>
    internal sealed record OwnRequest : IRequest<Result<ShardView>>;

    internal sealed class ShardNames
    {
        private readonly System.Collections.Generic.Dictionary<string, Guid> _ids = new(StringComparer.Ordinal) { ["Main"] = KnownShard };

        public Task<Guid> IdOfAsync(string name, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_ids.GetValueOrDefault(name));
        }
    }
}
