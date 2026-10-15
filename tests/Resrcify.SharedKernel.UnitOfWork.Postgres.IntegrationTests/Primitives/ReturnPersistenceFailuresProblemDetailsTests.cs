using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;
using Resrcify.SharedKernel.UnitOfWork.Primitives;
using Resrcify.SharedKernel.Web.Extensions;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Primitives;

/// <summary>
/// A command whose save breaks a constraint, sent through the mediator with
/// <see cref="UnitOfWorkPipelineOptions.ReturnPersistenceFailures"/> and answered as a service answers a failed result:
/// problem details with the status of the error's type, never a 500.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class ReturnPersistenceFailuresProblemDetailsTests(PostgresFixture postgres)
    : IClassFixture<PostgresFixture>
{
    private const string Path = "/members";

    public static TheoryData<string, int, string> Violations => new()
    {
        { "not null", StatusCodes.Status400BadRequest, PersistenceErrors.NotNullViolation.Code },
        { "check", StatusCodes.Status400BadRequest, PersistenceErrors.CheckViolation.Code },
        { "too long", StatusCodes.Status400BadRequest, PersistenceErrors.ValueTooLong.Code },
        { "foreign key", StatusCodes.Status409Conflict, PersistenceErrors.ForeignKeyViolation.Code },
    };

    [Theory]
    [MemberData(nameof(Violations))]
    public async Task ACommandBreakingAConstraint_ShouldBeAnsweredWithTheProblemDetailsOfItsError(
        string violation,
        int status,
        string code)
    {
        await using var app = await StartAsync();
        var shardId = await SaveShardAsync(app);
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            new Uri(Path, UriKind.Relative),
            CommandBreaking(violation, shardId));
        var result = await response.ToResultAsync();

        ((int)response.StatusCode).ShouldBe(status, violation);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(code, violation);
        await using var check = app.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<TestDbContext>().Members.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task ACommandBreakingNothing_ShouldBeSaved()
    {
        await using var app = await StartAsync();
        var shardId = await SaveShardAsync(app);
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync(
            new Uri(Path, UriKind.Relative),
            new AddMemberCommand(shardId, "valid", 1));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static AddMemberCommand CommandBreaking(
        string violation,
        Guid shardId)
        => violation switch
        {
            "not null" => new AddMemberCommand(shardId, null, 1),
            "check" => new AddMemberCommand(shardId, "minus", -1),
            "too long" => new AddMemberCommand(shardId, new string('x', Member.NicknameMaxLength + 1), 1),
            "foreign key" => new AddMemberCommand(Guid.NewGuid(), "orphan", 1),
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, null),
        };

    private async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(postgres.IsolatedDatabaseSettings());
        builder.Services.AddSingleton<EventTracker>();
        builder.Services.AddPostgresDbContext<TestDbContext>(builder.Configuration, db => { });
        builder.Services.AddMediator(cfg => cfg
            .RegisterServicesFromAssemblies(typeof(TestHost).Assembly)
            .AddOpenBehavior(typeof(UnitOfWorkPipelineBehavior<,>))
            .ConfigureUnitOfWork(uow => uow.ReturnPersistenceFailures = true));

        var app = builder.Build();
        app.MapPost(Path, AddMemberAsync);
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();

        await app.StartAsync();
        return app;
    }

    private static async Task<IResult> AddMemberAsync(
        AddMemberCommand command,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return result.IsSuccess
            ? Microsoft.AspNetCore.Http.Results.NoContent()
            : result.ToProblemDetails();
    }

    private static async Task<Guid> SaveShardAsync(WebApplication app)
    {
        var shard = new Shard(Guid.NewGuid(), $"shard-{Guid.NewGuid():N}");
        await using var scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestDbContext>().Shards.Add(shard);
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().SaveChangesAsync();
        return shard.Id;
    }
}
