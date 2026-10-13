using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Migrations;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Migrations;

/// <summary>
/// <c>AddMigrationsOnStartup</c> on in-memory SQLite: when it migrates, and that it does so before any hosted service
/// starts. (The PostgreSQL integration tests apply a real migration, from several instances at once.)
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MigrateOnStartupServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new($"Data Source=migrate-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    private readonly FakeTimeProvider _time = new();

    private readonly RecordingLogger<MigrateOnStartupService<TestDbContext>> _logger = new();

    [Fact]
    public async Task StartAsync_ShouldMigrate_BeforeAnyHostedServiceStarts()
    {
        await _connection.OpenAsync();
        var probe = new HistoryProbe(_connection);
        using var host = BuildHost("true", probe);

        await host.StartAsync();

        probe.HistoryExistedAtStart.ShouldBe(true);
        await host.StopAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("False")]
    public async Task StartAsync_ShouldNotMigrate_UnlessMigrationsRunIsTrue(string? run)
    {
        await _connection.OpenAsync();
        var probe = new HistoryProbe(_connection);
        using var host = BuildHost(run, probe);

        await host.StartAsync();

        probe.HistoryExistedAtStart.ShouldBe(false);
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_ShouldFail_WhenMigrationsRunIsNotABoolean()
    {
        await _connection.OpenAsync();
        using var host = BuildHost("yes", new HistoryProbe(_connection));

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => host.StartAsync());

        exception.Message.ShouldContain("Migrations:Run is 'yes'");
    }

    [Fact]
    public async Task StartAsync_ShouldMigrateAgain_WhenAnotherInstanceCreatedTheDatabaseAtTheSameMoment()
    {
        await _connection.OpenAsync();
        var probe = new HistoryProbe(_connection);
        var race = new LosingRace("42P04", losses: 2);
        using var host = BuildHost("true", probe, race);

        await StartAdvancingTimeAsync(host);

        race.Losses.ShouldBe(2);
        probe.HistoryExistedAtStart.ShouldBe(true);
        var warnings = Warnings();
        warnings.Count.ShouldBe(2);
        warnings[0].ShouldContain("SQLSTATE 42P04");
        warnings[0].ShouldContain("try 1 of 5 failed, trying again in 200 ms");
        warnings[1].ShouldContain("try 2 of 5 failed, trying again in 400 ms");
        _logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
        await host.StopAsync();
    }

    [Fact]
    public async Task StartAsync_ShouldFail_WhenItKeepsLosingTheRace()
    {
        await _connection.OpenAsync();
        var race = new LosingRace("42P07", losses: int.MaxValue);
        using var host = BuildHost("true", new HistoryProbe(_connection), race);

        var exception = await Should.ThrowAsync<DbException>(() => StartAdvancingTimeAsync(host));

        exception.SqlState.ShouldBe("42P07");
        race.Losses.ShouldBe(ConcurrentCreationRetry.MaxAttempts);
        Warnings().Count.ShouldBe(ConcurrentCreationRetry.MaxAttempts - 1);
        _logger.Entries.Count(entry => entry.Level == LogLevel.Critical).ShouldBe(1);
    }

    [Fact]
    public async Task StartAsync_ShouldFailAtOnce_WhenTheErrorIsNotACreationRace()
    {
        await _connection.OpenAsync();
        var race = new LosingRace("42601", losses: int.MaxValue);
        using var host = BuildHost("true", new HistoryProbe(_connection), race);

        var exception = await Should.ThrowAsync<DbException>(() => StartAdvancingTimeAsync(host));

        exception.SqlState.ShouldBe("42601");
        race.Losses.ShouldBe(1);
        Warnings().ShouldBeEmpty();
        _logger.Entries.Count(entry => entry.Level == LogLevel.Critical).ShouldBe(1);
    }

    [Fact]
    public void AddMigrationsOnStartup_ShouldBuildNothing_WhileRegistering()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(_ => throw new InvalidOperationException("built during registration"));

        services.AddMigrationsOnStartup<TestDbContext>(new ConfigurationBuilder().Build());

        services.ShouldContain(service => service.ServiceType == typeof(IHostedService));
    }

    public async ValueTask DisposeAsync()
        => await _connection.DisposeAsync();

    private IHost BuildHost(
        string? run,
        HistoryProbe probe,
        LosingRace? race = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Migrations:Run"] = run });
        builder.Services.AddLogging();
        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddSingleton<ILogger<MigrateOnStartupService<TestDbContext>>>(_logger);
        // Registered first, so it would start first if migrating didn't come before every start.
        builder.Services.AddSingleton<IHostedService>(probe);
        builder.Services.AddDbContext<TestDbContext>(options =>
        {
            options
                .UseSqlite(_connection.ConnectionString)
                .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
            if (race is not null)
                options.AddInterceptors(race);
        });
        builder.Services.AddMigrationsOnStartup<TestDbContext>(builder.Configuration);
        return builder.Build();
    }

    /// <summary>Starts <paramref name="host"/>, moving the fake clock on while it waits to migrate again.</summary>
    private async Task StartAdvancingTimeAsync(IHost host)
    {
        var starting = host.StartAsync();
        for (var turn = 0; turn < 2_000 && !starting.IsCompleted; turn++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), CancellationToken.None);
            if (!starting.IsCompleted)
                _time.Advance(TimeSpan.FromMilliseconds(100));
        }

        await starting;
    }

    private List<string> Warnings()
        => [.. _logger.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message)];

    /// <summary>
    /// Fails the first <c>losses</c> commands the context runs with <c>sqlState</c>, as the database answers an instance
    /// that lost the race to create the database or the history table. A failed command ends its try, so each loss is
    /// one try.
    /// </summary>
    private sealed class LosingRace(
        string sqlState,
        int losses)
        : DbCommandInterceptor
    {
        private int _losses;

        public int Losses
            => Volatile.Read(ref _losses);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            LoseWhileLosing();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            LoseWhileLosing();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            LoseWhileLosing();
            return ValueTask.FromResult(result);
        }

        private void LoseWhileLosing()
        {
            if (Losses >= losses)
                return;

            Interlocked.Increment(ref _losses);
            throw DbExceptions.WithSqlState(sqlState);
        }
    }

    /// <summary>A hosted service that looks, when it starts, whether the migrations history table exists.</summary>
    private sealed class HistoryProbe(SqliteConnection connection)
        : IHostedService
    {
        public bool? HistoryExistedAtStart { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'";
            HistoryExistedAtStart = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L) == 1;
        }

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
