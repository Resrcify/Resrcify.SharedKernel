using System;
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
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MediatorEndpointExtensionsTests
{
    private static readonly Guid KnownShard = Guid.Parse("2b8f4a8e-1d4e-4a59-9a7e-0f3c2d1b6a10");

    private readonly ISender _sender = Substitute.For<ISender>();

    [Fact]
    public async Task MapPostCommand_ShouldSendTheCommandBuiltFromTheRouteAndBody_AndAnswer204()
    {
        RenameShard? sent = null;
        _sender.Send(Arg.Do<IRequest<Result>>(command => sent = (RenameShard)command), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        await using var app = await StartAsync(endpoints => endpoints.MapPostCommand<RenameRequest, RenameShard>(
            "/shards/{shardId:guid}/name",
            request => new RenameShard(request.ShardId, request.Body.Name)));

        using var response = await app.GetTestClient().PostAsJsonAsync($"/shards/{KnownShard}/name", new RenameBody("Main"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        sent.ShouldBe(new RenameShard(KnownShard, "Main"));
    }

    [Fact]
    public async Task MapPostCommand_ShouldAnswerProblemDetails_WhenTheCommandFails()
    {
        _sender.Send(Arg.Any<IRequest<Result>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new Error("Shard.NotFound", "No such shard", ErrorType.NotFound)));
        await using var app = await StartAsync(endpoints => endpoints.MapPostCommand<RenameRequest, RenameShard>(
            "/shards/{shardId:guid}/name",
            request => new RenameShard(request.ShardId, request.Body.Name)));

        using var response = await app.GetTestClient().PostAsJsonAsync($"/shards/{KnownShard}/name", new RenameBody("Main"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Shard.NotFound");
    }

    [Fact]
    public async Task MapPostCommand_WithAResponse_ShouldAnswer200WithIt()
    {
        _sender.Send(Arg.Any<IRequest<Result<ShardView>>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ShardView(KnownShard, "Main")));
        await using var app = await StartAsync(endpoints => endpoints.MapPostCommand<CreateRequest, CreateShard, ShardView>(
            "/shards",
            request => new CreateShard(request.Body.Name)));

        using var response = await app.GetTestClient().PostAsJsonAsync("/shards", new RenameBody("Main"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ShardView>()).ShouldBe(new ShardView(KnownShard, "Main"));
    }

    [Fact]
    public async Task MapDeleteCommand_ShouldUseTheDeleteMethod()
    {
        _sender.Send(Arg.Any<IRequest<Result>>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        await using var app = await StartAsync(endpoints => endpoints.MapDeleteCommand<ShardRoute, DeleteShard>(
            "/shards/{shardId:guid}",
            request => new DeleteShard(request.ShardId)));

        var shard = new Uri($"/shards/{KnownShard}", UriKind.Relative);
        using var deleted = await app.GetTestClient().DeleteAsync(shard);
        using var posted = await app.GetTestClient().PostAsync(shard, content: null);

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        posted.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task MapGetQuery_ShouldAnswer200WithThePartOfTheResultPicked()
    {
        GetShard? sent = null;
        _sender.Send(Arg.Do<IRequest<Result<ShardDetails>>>(query => sent = (GetShard)query), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ShardDetails(new ShardView(KnownShard, "Main"), MemberCount: 50)));
        await using var app = await StartAsync(endpoints => endpoints.MapGetQuery<ShardRoute, GetShard, ShardDetails, ShardView>(
            "/shards/{shardId:guid}",
            request => new GetShard(request.ShardId),
            details => details.Shard));

        var shard = await app.GetTestClient().GetFromJsonAsync<ShardView>($"/shards/{KnownShard}");

        shard.ShouldBe(new ShardView(KnownShard, "Main"));
        sent.ShouldBe(new GetShard(KnownShard));
    }

    [Fact]
    public async Task MapGetQuery_WithoutARequest_ShouldAnswer200WithTheResult()
    {
        _sender.Send(Arg.Any<IRequest<Result<ShardView[]>>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<ShardView[]>([new ShardView(KnownShard, "Main")]));
        await using var app = await StartAsync(endpoints => endpoints.MapGetQuery<GetAllShards, ShardView[]>(
            "/shards",
            () => new GetAllShards()));

        var shards = await app.GetTestClient().GetFromJsonAsync<ShardView[]>("/shards");

        shards.ShouldBe([new ShardView(KnownShard, "Main")]);
    }

    [Fact]
    public async Task MapPostCommand_ShouldDescribeItsResponsesForOpenApi()
    {
        await using var app = await StartAsync(endpoints => endpoints
            .MapPostCommand<RenameRequest, RenameShard>("/shards/{shardId:guid}/name", request => new RenameShard(request.ShardId, request.Body.Name))
            .WithName("Shards.Rename"));

        var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "Shards.Rename");
        var statusCodes = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(produces => produces.StatusCode);

        statusCodes.ShouldBe([204, 400, 404, 409, 500], ignoreOrder: true);
    }

    private async Task<WebApplication> StartAsync(Action<IEndpointRouteBuilder> map)
        => await TestHosts.StartAsync(services => services.AddSingleton(_sender), app => map(app));

    internal sealed record RenameBody(string Name);

    internal sealed record RenameRequest([FromRoute] Guid ShardId, [FromBody] RenameBody Body);

    internal sealed record CreateRequest([FromBody] RenameBody Body);

    internal sealed record ShardRoute([FromRoute] Guid ShardId);

    internal sealed record RenameShard(Guid ShardId, string Name) : ICommand;

    internal sealed record DeleteShard(Guid ShardId) : ICommand;

    internal sealed record CreateShard(string Name) : ICommand<ShardView>;

    internal sealed record GetShard(Guid ShardId) : IQuery<ShardDetails>;

    internal sealed record GetAllShards : IQuery<ShardView[]>;

    internal sealed record ShardView(Guid Id, string Name);

    internal sealed record ShardDetails(ShardView Shard, int MemberCount);
}
