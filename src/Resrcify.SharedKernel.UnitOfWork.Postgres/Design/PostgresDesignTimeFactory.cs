using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.Design;

/// <summary>
/// The design-time factory <c>dotnet ef</c> uses to create <typeparamref name="TContext"/>, reading the same settings
/// the service runs with: the Web project's <c>appsettings.json</c> and <c>appsettings.{environment}.json</c>, then
/// environment variables (<c>Database__Host</c>), then the arguments after <c>--</c>
/// (<c>dotnet ef database update -- --Database:Host=localhost</c>). A service needs one line:
/// <code>internal sealed class ShardDbContextFactory : PostgresDesignTimeFactory&lt;ShardDbContext&gt;;</code>
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The Web project is found from the Persistence project's name (<c>Titan.Shard.Persistence</c> →
/// <c>Titan.Shard.Web</c>), looking up from <c>dotnet ef</c>'s working directory and the build output, as a sibling
/// folder or under <c>src/</c>. Override <see cref="SettingsDirectory"/> to point elsewhere.</item>
/// <item>The environment is <c>ASPNETCORE_ENVIRONMENT</c>, else <c>DOTNET_ENVIRONMENT</c>, else <c>Development</c>.</item>
/// <item>Missing values get placeholders (<c>localhost</c>, the context's name, <c>postgres</c>), so
/// <c>migrations add</c>, which doesn't connect, works without any settings; <c>database update</c> needs real ones.</item>
/// <item>Override <see cref="Configure(PostgresDbContextBuilder)"/> with the Npgsql options the service's registration
/// uses (a migrations table, enums), <see cref="ConfigureOptions"/> to change the settings read (a local port), and
/// <see cref="CreateDbContext(DbContextOptions{TContext})"/> when the context has no public
/// <c>(DbContextOptions&lt;TContext&gt;)</c> constructor.</item>
/// </list>
/// </remarks>
public abstract class PostgresDesignTimeFactory<TContext>
    : IDesignTimeDbContextFactory<TContext>
    where TContext : DbContext
{
    /// <summary>The environment the settings are read for.</summary>
    protected virtual string EnvironmentName
        => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Development";

    /// <summary>The Web project whose settings are read; derived from the context's assembly name.</summary>
    protected virtual string? WebProjectName
        => DesignTimeSettings.WebProjectFor(typeof(TContext).Assembly.GetName().Name);

    /// <summary>The directory holding <c>appsettings.json</c>; <see langword="null"/> reads environment variables and arguments only.</summary>
    protected virtual string? SettingsDirectory
        => DesignTimeSettings.Find(
            WebProjectName,
            [Directory.GetCurrentDirectory(), AppContext.BaseDirectory]);

    public TContext CreateDbContext(string[] args)
    {
        var builder = new PostgresDbContextBuilder();
        Configure(builder);

        var settings = ReadOptions(BuildConfiguration(args ?? []), builder.SectionName);
        ConfigureOptions(settings);

        var options = new DbContextOptionsBuilder<TContext>();
        builder.Apply(options, builder.ConnectionString(settings));
        return CreateDbContext(options.Options);
    }

    /// <summary>The Npgsql and EF Core options shared with the service's <c>AddPostgresDbContext</c>.</summary>
    protected virtual void Configure(PostgresDbContextBuilder builder)
    {
    }

    /// <summary>Changes the settings read, e.g. to reach the database on a port the host maps.</summary>
    protected virtual void ConfigureOptions(PostgresOptions options)
    {
    }

    /// <summary>Creates the context; by default through its public <c>(DbContextOptions&lt;TContext&gt;)</c> constructor.</summary>
    protected virtual TContext CreateDbContext(DbContextOptions<TContext> options)
        => (TContext)(Activator.CreateInstance(typeof(TContext), options)
            ?? throw new InvalidOperationException($"{typeof(TContext).Name} could not be created."));

    private IConfiguration BuildConfiguration(string[] args)
    {
        var configuration = new ConfigurationBuilder();
        if (SettingsDirectory is { } directory)
        {
            configuration
                .SetBasePath(directory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{EnvironmentName}.json", optional: true);
        }

        return configuration
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
    }

    private static PostgresOptions ReadOptions(
        IConfiguration configuration,
        string sectionName)
    {
        var options = new PostgresOptions();
        configuration.GetSection(sectionName).Bind(options);
        if (string.IsNullOrWhiteSpace(options.Host))
            options.Host = "localhost";
        if (string.IsNullOrWhiteSpace(options.Database))
            options.Database = typeof(TContext).Name;
        if (string.IsNullOrWhiteSpace(options.Username))
            options.Username = "postgres";
        return options;
    }
}
