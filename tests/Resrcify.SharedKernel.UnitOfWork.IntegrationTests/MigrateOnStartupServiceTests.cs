using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Migrations;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Models;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Resrcify.SharedKernel.UnitOfWork.IntegrationTests;

/// <summary><c>AddMigrationsOnStartup</c> against a real PostgreSQL.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
[Trait("Category", "Integration")]
public sealed class MigrateOnStartupServiceTests(
    PostgresFixture postgres,
    ITestOutputHelper output)
    : IClassFixture<PostgresFixture>
{
    /// <summary>
    /// The instances that create the database at once: enough for a race (one wins, the others collide) and for the
    /// winner not to be the only one; few enough that the run opens a few dozen connections, not hundreds.
    /// </summary>
    private const int RaceInstances = 4;

    [Fact]
    public async Task StartAsync_ShouldApplyThePendingMigrations_BeforeTheHostRuns()
    {
        var connectionString = postgres.CreateIsolatedConnectionString();
        using var host = BuildHost(connectionString, run: "true");

        await host.StartAsync();

        (await AppliedMigrationsAsync(host)).ShouldBe(["20261006000000_CreateLedgers"]);
        await host.StopAsync();
        await DropAsync(host);
    }

    [Fact]
    public async Task StartAsync_ShouldNotMigrate_WhenMigrationsRunIsOff()
    {
        var connectionString = postgres.CreateIsolatedConnectionString();
        using var host = BuildHost(connectionString, run: null);

        await host.StartAsync();

        await using var scope = host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<MigratedDbContext>().Database.CanConnectAsync()).ShouldBeFalse();
        await host.StopAsync();
    }

    /// <summary>
    /// Several pods start together after a deploy. EF Core 9+ locks the database around <c>Migrate</c> (Npgsql:
    /// <c>LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE</c>), so the second waits and finds the work done.
    /// </summary>
    [Fact]
    public async Task StartAsync_ShouldMigrateOnce_WhenSeveralInstancesStartAtOnce()
    {
        var connectionString = postgres.CreateIsolatedConnectionString();
        var hosts = Enumerable.Range(0, 3).Select(_ => BuildHost(connectionString, run: "true")).ToList();

        await Task.WhenAll(hosts.Select(host => host.StartAsync()));

        (await AppliedMigrationsAsync(hosts[0])).ShouldBe(["20261006000000_CreateLedgers"]);
        await using (var scope = hosts[0].Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<MigratedDbContext>().Ledgers.CountAsync()).ShouldBe(0);
        foreach (var host in hosts)
            await host.StopAsync();
        await DropAsync(hosts[0]);
        hosts.ForEach(host => host.Dispose());
    }

    /// <summary>
    /// The deploy of a new service: its database doesn't exist yet and every pod starts at once, each creating the
    /// database (EF Core does that before it takes the migration lock). The instances are held at their
    /// <c>CREATE DATABASE</c> until all of them have reached it (<see cref="DatabaseCreationGate"/>), so they create it at
    /// once on every run, then let go together (the statements collide on the server) or one first (the others then get
    /// <c>42P04 duplicate_database</c>, which the service must take as a lost race and migrate again).
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("ledgers", false)]
    [InlineData("ledgers", true)]
    public async Task StartAsync_ShouldStartEveryInstance_WhenTheyCreateTheDatabaseAtOnce(
        string? historySchema,
        bool oneFirst)
    {
        using var logs = new FakeLoggerProvider();
        var gate = new DatabaseCreationGate(RaceInstances, oneFirst ? CreationOrder.OneFirst : CreationOrder.Together);
        var connectionString = postgres.CreateIsolatedConnectionString();
        var hosts = Enumerable.Range(0, RaceInstances)
            .Select(_ => BuildHost(connectionString, run: "true", historySchema, logs, gate))
            .ToList();

        var starts = hosts.Select(host => host.StartAsync()).ToList();
        var failures = await FailuresOfAsync(starts);

        var retries = LostRaces(logs);
        output.WriteLine($"{failures.Count} of {RaceInstances} instances failed to start");
        failures.ForEach(failure => output.WriteLine(Describe(failure)));
        retries.ForEach(retry => output.WriteLine(retry));
        gate.HeldTogether.ShouldBeTrue();
        gate.Arrived.ShouldBe(RaceInstances);
        failures.ShouldBeEmpty();
        (await AppliedMigrationsAsync(hosts[0])).ShouldBe(["20261006000000_CreateLedgers"]);
        if (oneFirst)
            retries.Count(retry => retry.Contains("SQLSTATE 42P04", StringComparison.Ordinal)).ShouldBe(RaceInstances - 1);
        await StopAsync(hosts, starts);
        await DropAsync(hosts[0]);
        hosts.ForEach(host => host.Dispose());
    }

    private static async Task<List<Exception>> FailuresOfAsync(List<Task> starts)
    {
        try
        {
            await Task.WhenAll(starts);
        }
        catch (Exception)
        {
            // Every failure is read from its own task below.
        }

        return starts
            .Where(start => start.IsFaulted)
            .Select(start => start.Exception!.GetBaseException())
            .ToList();
    }

    private static List<string> LostRaces(FakeLoggerProvider logs)
        => logs.Collector.GetSnapshot()
            .Where(entry => entry.Level == LogLevel.Warning)
            .Where(entry => entry.Category?.Contains("MigrateOnStartupService", StringComparison.Ordinal) == true)
            .Select(entry => entry.Message)
            .ToList();

    private static async Task StopAsync(
        List<IHost> hosts,
        List<Task> starts)
    {
        for (var index = 0; index < hosts.Count; index++)
        {
            if (starts[index].IsCompletedSuccessfully)
                await hosts[index].StopAsync();
        }
    }

    private static string Describe(Exception exception)
    {
        var sqlState = exception is DbException dbException ? dbException.SqlState : null;
        var frames = (exception.StackTrace ?? string.Empty)
            .Split(Environment.NewLine)
            .Select(frame => frame.Trim())
            .Where(frame => frame.Contains("EntityFrameworkCore", StringComparison.Ordinal))
            .Distinct()
            .Take(5);
        return $"{exception.GetType().FullName} SqlState={sqlState} {exception.Message} | {string.Join(" | ", frames)}";
    }

    private static IHost BuildHost(
        string connectionString,
        string? run,
        string? historySchema = null,
        FakeLoggerProvider? logs = null,
        DatabaseCreationGate? gate = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Migrations:Run"] = run });
        builder.Services.AddLogging(logging =>
        {
            if (logs is not null)
                logging.AddProvider(logs);
        });
        builder.Services.AddDbContext<MigratedDbContext>(options =>
        {
            options
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, historySchema))
                // The migration is written by hand, without a model snapshot to compare with.
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
            if (gate is not null)
                options.AddInterceptors(gate);
        });
        builder.Services.AddMigrationsOnStartup<MigratedDbContext>(builder.Configuration);
        return builder.Build();
    }

    private static async Task<List<string>> AppliedMigrationsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return [.. await scope.ServiceProvider.GetRequiredService<MigratedDbContext>().Database.GetAppliedMigrationsAsync()];
    }

    private static async Task DropAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MigratedDbContext>().Database.EnsureDeletedAsync();
    }
}
