using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.IntegrationTests.Fixtures;
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
    /// <summary>Overrides <see cref="RaceIterations"/> for a long stress run.</summary>
    private const string RaceIterationsVariable = "RESRCIFY_MIGRATION_RACE_ITERATIONS";

    private const int RaceIterations = 5;

    private const int RaceInstances = 8;

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
    /// database (EF Core does that before it takes the migration lock). Repeated on a fresh database per round.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("ledgers")]
    public async Task StartAsync_ShouldStartEveryInstance_WhenTheyCreateTheDatabaseAtOnce(string? historySchema)
    {
        var failures = new List<Exception>();
        var rounds = RaceRounds();
        using var logs = new CapturingLoggerProvider();

        for (var round = 0; round < rounds; round++)
            failures.AddRange(await StartAtOnceAsync(round, historySchema, logs));

        var retries = logs.Entries
            .Where(entry => entry.Level == LogLevel.Warning)
            .Where(entry => entry.Category.Contains("MigrateOnStartupService", StringComparison.Ordinal))
            .ToList();
        output.WriteLine($"{failures.Count} of {rounds * RaceInstances} instances failed to start in {rounds} rounds");
        output.WriteLine($"{retries.Count} tries lost a creation race and were retried");
        retries.ForEach(retry => output.WriteLine(retry.Message));
        failures.ShouldBeEmpty();
    }

    private async Task<List<Exception>> StartAtOnceAsync(
        int round,
        string? historySchema,
        CapturingLoggerProvider logs)
    {
        var connectionString = postgres.CreateIsolatedConnectionString();
        var hosts = Enumerable.Range(0, RaceInstances)
            .Select(_ => BuildHost(connectionString, run: "true", historySchema, logs))
            .ToList();
        var starts = hosts.Select(host => host.StartAsync()).ToList();
        try
        {
            await Task.WhenAll(starts);
        }
        catch (Exception)
        {
            // Every failure is read from its own task below.
        }

        var failures = starts
            .Where(start => start.IsFaulted)
            .Select(start => start.Exception!.GetBaseException())
            .ToList();
        failures.ForEach(failure => output.WriteLine($"round {round}: {Describe(failure)}"));
        (await AppliedMigrationsAsync(hosts[0])).ShouldBe(["20261006000000_CreateLedgers"]);
        await StopAsync(hosts, starts);
        await DropAsync(hosts[0]);
        hosts.ForEach(host => host.Dispose());
        return failures;
    }

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

    private static int RaceRounds()
        => int.TryParse(
            Environment.GetEnvironmentVariable(RaceIterationsVariable),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var rounds)
            ? rounds
            : RaceIterations;

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
        CapturingLoggerProvider? logs = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Migrations:Run"] = run });
        builder.Services.AddLogging(logging =>
        {
            if (logs is not null)
                logging.AddProvider(logs);
        });
        builder.Services.AddDbContext<MigratedDbContext>(options => options
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, historySchema))
            // The migration is written by hand, without a model snapshot to compare with.
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));
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
