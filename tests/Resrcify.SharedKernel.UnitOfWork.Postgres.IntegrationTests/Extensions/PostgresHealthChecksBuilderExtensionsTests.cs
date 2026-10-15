using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Resrcify.SharedKernel.Abstractions.Health;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.IntegrationTests.Extensions;

/// <summary><c>AddHealthChecks().AddPostgres&lt;TContext&gt;()</c> against a real PostgreSQL, and one that isn't there.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class PostgresHealthChecksBuilderExtensionsTests(PostgresFixture postgres)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AddPostgres_ShouldBeHealthyAndReady_WhenTheDatabaseAnswers()
    {
        await using var host = await TestHost.CreateAsync(
            postgres,
            db => { },
            services => services.AddHealthChecks().AddPostgres<TestDbContext>());

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        var entry = report.Entries["postgres"];
        entry.Status.ShouldBe(HealthStatus.Healthy);
        entry.Tags.ShouldBe([HealthTags.Ready]);
    }

    [Fact]
    public async Task AddPostgres_ShouldBeUnhealthy_WhenTheDatabaseDoesNotAnswer()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Host"] = "127.0.0.1",
            ["Database:Port"] = "1",
            ["Database:Database"] = "nowhere",
            ["Database:Username"] = "nobody",
            ["Database:Password"] = "nothing",
        });
        builder.Services.AddPostgresDbContext<TestDbContext>(builder.Configuration);
        builder.Services.AddHealthChecks().AddPostgres<TestDbContext>(name: "database", tags: ["custom"]);
        using var host = builder.Build();

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        var entry = report.Entries["database"];
        entry.Status.ShouldBe(HealthStatus.Unhealthy);
        entry.Description.ShouldNotBeNull().ShouldContain("TestDbContext's PostgreSQL database didn't answer");
        entry.Tags.ShouldBe(["custom"]);
    }

    [Fact]
    public async Task AddPostgres_ShouldFailTheHostsStart_WithoutTheContext()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddHealthChecks().AddPostgres<TestDbContext>();
        using var host = builder.Build();

        var thrown = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        thrown.Message.ShouldContain("AddPostgresDbContext<TestDbContext>()");
    }
}
