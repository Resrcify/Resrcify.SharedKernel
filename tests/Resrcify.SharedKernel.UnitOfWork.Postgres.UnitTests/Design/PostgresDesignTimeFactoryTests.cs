using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Design;
using Resrcify.SharedKernel.UnitOfWork.Postgres.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.UnitTests.Design;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class PostgresDesignTimeFactoryTests : IDisposable
{
    private readonly string _settings = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"designtime-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public void CreateDbContext_ShouldReadTheWebProjectsSettings_ForTheEnvironment()
    {
        WriteSettings("appsettings.json", """
            { "Database": { "Host": "sharddb", "Port": "5432", "Database": "ShardDb", "Username": "ShardUser", "Password": "p;w" } }
            """);
        WriteSettings("appsettings.Test.json", """{ "Database": { "Host": "localhost", "Port": "5433" } }""");

        using var context = new Factory(_settings, "Test").CreateDbContext([]);

        var connection = Connection(context);
        connection.Host.ShouldBe("localhost");
        connection.Port.ShouldBe(5433);
        connection.Database.ShouldBe("ShardDb");
        connection.Password.ShouldBe("p;w");
    }

    [Fact]
    public void CreateDbContext_ShouldLetTheArgumentsWin()
    {
        WriteSettings("appsettings.json", """{ "Database": { "Host": "sharddb" } }""");

        using var context = new Factory(_settings).CreateDbContext(["--Database:Host=127.0.0.1"]);

        Connection(context).Host.ShouldBe("127.0.0.1");
    }

    [Fact]
    public void CreateDbContext_ShouldUsePlaceholders_WithoutAnySettings()
    {
        using var context = new Factory(settingsDirectory: null).CreateDbContext([]);

        var connection = Connection(context);
        connection.Host.ShouldBe("localhost");
        connection.Port.ShouldBe(5432);
        connection.Database.ShouldBe(nameof(TestDbContext));
        connection.Username.ShouldBe("postgres");
    }

    [Fact]
    public void CreateDbContext_ShouldApplyTheOverrides()
    {
        WriteSettings("appsettings.json", """{ "Db": { "Host": "sharddb" } }""");

        using var context = new Factory(
            _settings,
            configure: db => db
                .FromSection("Db")
                .ConfigureConnection(connection => connection.ApplicationName = "migrations"),
            configureOptions: options => options.Port = 5438)
            .CreateDbContext([]);

        var connection = Connection(context);
        connection.Host.ShouldBe("sharddb");
        connection.Port.ShouldBe(5438);
        connection.ApplicationName.ShouldBe("migrations");
    }

    public void Dispose()
        => Directory.Delete(_settings, recursive: true);

    private void WriteSettings(string file, string json)
        => File.WriteAllText(Path.Combine(_settings, file), json);

    private static NpgsqlConnectionStringBuilder Connection(DbContext context)
        => new(context.Database.GetConnectionString());

    private sealed class Factory(
        string? settingsDirectory,
        string environment = "Development",
        Action<PostgresDbContextBuilder>? configure = null,
        Action<PostgresOptions>? configureOptions = null)
        : PostgresDesignTimeFactory<TestDbContext>
    {
        protected override string? SettingsDirectory => settingsDirectory;

        protected override string EnvironmentName => environment;

        protected override void Configure(PostgresDbContextBuilder builder)
            => configure?.Invoke(builder);

        protected override void ConfigureOptions(PostgresOptions options)
            => configureOptions?.Invoke(options);

        protected override TestDbContext CreateDbContext(DbContextOptions<TestDbContext> options)
            => new(options);
    }
}
