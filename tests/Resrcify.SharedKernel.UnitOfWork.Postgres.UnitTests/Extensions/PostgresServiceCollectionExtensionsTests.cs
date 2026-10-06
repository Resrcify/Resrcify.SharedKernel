using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;
using Resrcify.SharedKernel.UnitOfWork.Abstractions;
using Resrcify.SharedKernel.UnitOfWork.Interceptors;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Configuration;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Postgres.Outbox;
using Resrcify.SharedKernel.UnitOfWork.Postgres.UnitTests.Models;
using Resrcify.SharedKernel.UnitOfWork.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.Postgres.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class PostgresServiceCollectionExtensionsTests
{
    private const string AwkwardPassword = "p;a's\"s w=rd";

    [Fact]
    public async Task AddPostgresDbContext_ShouldBuildTheConnectionStringFromTheDatabaseSection_WithAPasswordThatBreaksInterpolation()
    {
        await using var provider = Build(Settings(password: AwkwardPassword));

        var connection = new NpgsqlConnectionStringBuilder(provider.GetPostgresConnectionString<TestDbContext>());

        connection.Host.ShouldBe("db.local");
        connection.Port.ShouldBe(5433);
        connection.Database.ShouldBe("ShardDb");
        connection.Username.ShouldBe("ShardUser");
        connection.Password.ShouldBe(AwkwardPassword);
    }

    [Fact]
    public async Task AddPostgresDbContext_ShouldPointTheContextAtTheBuiltConnectionString()
    {
        await using var provider = Build(Settings());
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        context.Database.GetConnectionString().ShouldBe(provider.GetPostgresConnectionString<TestDbContext>());
    }

    [Fact]
    public async Task AddPostgresDbContext_ShouldSetTheCommandTimeout_WhenConfigured()
    {
        var settings = Settings();
        settings["Database:CommandTimeoutInSeconds"] = "500";
        await using var provider = Build(settings);

        new NpgsqlConnectionStringBuilder(provider.GetPostgresConnectionString<TestDbContext>())
            .CommandTimeout
            .ShouldBe(500);
    }

    [Fact]
    public async Task AddPostgresDbContext_ShouldNameEveryMissingKey_WhenTheSectionIsIncomplete()
    {
        await using var provider = Build(new Dictionary<string, string?> { ["Database:Host"] = "db.local" });

        var exception = Should.Throw<OptionsValidationException>(provider.GetPostgresConnectionString<TestDbContext>);

        exception.Failures.ShouldContain("Database:Database is required.");
        exception.Failures.ShouldContain("Database:Username is required.");
        exception.Failures.ShouldContain("Database:Password is required.");
        exception.Failures.ShouldNotContain("Database:Host is required.");
    }

    [Fact]
    public async Task AddPostgresDbContext_ShouldFailTheHostsStart_WhenTheSectionIsIncomplete()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Port"] = "70000" });
        builder.Services.AddPostgresDbContext<TestDbContext>(builder.Configuration);
        using var host = builder.Build();

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Failures.ShouldContain("Database:Host is required.");
        exception.Failures.ShouldContain("Database:Port must be a port (1-65535); it is 70000.");
    }

    [Fact]
    public async Task AddPostgresDbContext_ShouldReadAnotherSection_WhenToldTo()
    {
        var settings = Settings();
        settings["Postgres:Host"] = "other.local";
        settings["Postgres:Database"] = "OtherDb";
        settings["Postgres:Username"] = "OtherUser";
        settings["Postgres:Password"] = "secret";
        await using var provider = Build(settings, db => db.FromSection("Postgres"));

        new NpgsqlConnectionStringBuilder(provider.GetPostgresConnectionString<TestDbContext>())
            .Host
            .ShouldBe("other.local");
    }

    [Fact]
    public async Task AddPostgresDbContext_ShouldKeepEachContextsSettingsApart()
    {
        var settings = Settings();
        settings["Other:Host"] = "other.local";
        settings["Other:Database"] = "OtherDb";
        settings["Other:Username"] = "OtherUser";
        settings["Other:Password"] = "secret";
        var services = new ServiceCollection();
        var configuration = Configuration(settings);
        services.AddPostgresDbContext<TestDbContext>(configuration);
        services.AddPostgresDbContext<OtherDbContext>(configuration, db => db.FromSection("Other"));
        await using var provider = services.BuildServiceProvider();

        new NpgsqlConnectionStringBuilder(provider.GetPostgresConnectionString<TestDbContext>()).Host.ShouldBe("db.local");
        new NpgsqlConnectionStringBuilder(provider.GetPostgresConnectionString<OtherDbContext>()).Host.ShouldBe("other.local");
    }

    [Fact]
    public async Task AddPostgresDbContext_ShouldAddTheEntityInterceptorsFromTheContainer()
    {
        await using var provider = Build(Settings());
        await using var scope = provider.CreateAsyncScope();

        var interceptors = Interceptors(scope.ServiceProvider.GetRequiredService<TestDbContext>());

        interceptors.ShouldContain(provider.GetRequiredService<UpdateAuditableEntitiesInterceptor>());
        interceptors.ShouldContain(provider.GetRequiredService<UpdateDeletableEntitiesInterceptor>());
        interceptors.OfType<InsertOutboxMessagesInterceptor>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WithOutbox_ShouldAddTheOutboxInterceptor_WithTheRegisteredSerializer()
    {
        await using var provider = Build(Settings(), db => db.WithOutbox());
        await using var scope = provider.CreateAsyncScope();

        var interceptors = Interceptors(scope.ServiceProvider.GetRequiredService<TestDbContext>());

        interceptors.ShouldContain(provider.GetRequiredService<InsertOutboxMessagesInterceptor>());
        provider.GetRequiredService<IOutboxSerializer>().ShouldBeOfType<SystemTextJsonOutboxSerializer>();
        provider.GetService<IOutboxInsertStrategy>().ShouldBeNull();
    }

    [Fact]
    public async Task WithOutbox_ShouldInsertWithOnConflictDoNothing_WhenAsked()
    {
        await using var provider = Build(Settings(), db => db.WithOutbox(outbox => outbox.OnConflictDoNothing = true));

        provider.GetRequiredService<IOutboxInsertStrategy>().ShouldBeOfType<PostgresOnConflictOutboxInsertStrategy>();
    }

    [Fact]
    public async Task WithOutbox_ShouldLeaveTheOutboxOutOfAContextWithoutIt()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(Settings());
        services.AddPostgresDbContext<TestDbContext>(configuration, db => db.WithOutbox());
        services.AddPostgresDbContext<OtherDbContext>(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Interceptors(scope.ServiceProvider.GetRequiredService<OtherDbContext>())
            .OfType<InsertOutboxMessagesInterceptor>()
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task RetryOnFailure_ShouldUseARetryingExecutionStrategy()
    {
        await using var withRetries = Build(Settings(), db => db.RetryOnFailure());
        await using var withoutRetries = Build(Settings());
        await using var retryingScope = withRetries.CreateAsyncScope();
        await using var plainScope = withoutRetries.CreateAsyncScope();

        retryingScope.ServiceProvider.GetRequiredService<TestDbContext>().Database
            .CreateExecutionStrategy().RetriesOnFailure.ShouldBeTrue();
        plainScope.ServiceProvider.GetRequiredService<TestDbContext>().Database
            .CreateExecutionStrategy().RetriesOnFailure.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfigureConnection_ShouldChangeTheBuiltConnectionString()
    {
        await using var provider = Build(Settings(), db => db.ConfigureConnection(connection => connection.ApplicationName = "shard"));

        new NpgsqlConnectionStringBuilder(provider.GetPostgresConnectionString<TestDbContext>())
            .ApplicationName
            .ShouldBe("shard");
    }

    [Fact]
    public async Task ConfigureNpgsqlAndConfigureDbContext_ShouldApplyTheServicesOwnOptions()
    {
        await using var provider = Build(Settings(), db => db
            .ConfigureNpgsql(npgsql => npgsql.MigrationsHistoryTable("history", "meta"))
            .ConfigureDbContext(options => options.EnableDetailedErrors()));
        await using var scope = provider.CreateAsyncScope();

        var options = scope.ServiceProvider.GetRequiredService<TestDbContext>().GetService<IDbContextOptions>();

        RelationalOptionsExtension.Extract(options).MigrationsHistoryTableName.ShouldBe("history");
        options.FindExtension<CoreOptionsExtension>().ShouldNotBeNull().DetailedErrorsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task AddPostgresDbContext_ShouldRegisterTheContextsUnitOfWork()
    {
        await using var provider = Build(Settings());
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ShouldBeOfType<UnitOfWork<TestDbContext>>();
    }

    [Fact]
    public void WithOutboxWakeUp_ShouldThrow_WithoutWithOutbox()
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<InvalidOperationException>(() => services.AddPostgresDbContext<TestDbContext>(
            Configuration(Settings()),
            db => db.WithOutboxWakeUp()));

        exception.Message.ShouldContain("WithOutbox()");
    }

    [Fact]
    public async Task WithOutboxWakeUp_ShouldRegisterTheNotifierAndTheListener()
    {
        await using var provider = Build(Settings(), db => db.WithOutbox().WithOutboxWakeUp());

        provider.GetServices<IOutboxSaveObserver>().ShouldHaveSingleItem().ShouldBeOfType<PostgresOutboxNotifier<TestDbContext>>();
        provider.GetServices<IHostedService>().OfType<PostgresOutboxListener<TestDbContext>>().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(-1, 1000, 30_000)]
    [InlineData(50, 0, 30_000)]
    [InlineData(50, 1000, 500)]
    public void WithOutboxWakeUp_ShouldRefuseTimesThatMakeNoSense(
        int debounceMilliseconds,
        int reconnectMilliseconds,
        int maxReconnectMilliseconds)
    {
        var builder = new PostgresDbContextBuilder();

        Should.Throw<ArgumentOutOfRangeException>(() => builder.WithOutboxWakeUp(wakeUp =>
        {
            wakeUp.Debounce = TimeSpan.FromMilliseconds(debounceMilliseconds);
            wakeUp.ReconnectDelay = TimeSpan.FromMilliseconds(reconnectMilliseconds);
            wakeUp.MaxReconnectDelay = TimeSpan.FromMilliseconds(maxReconnectMilliseconds);
        }));
    }

    private static ServiceProvider Build(
        Dictionary<string, string?> settings,
        Action<PostgresDbContextBuilder>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgresDbContext<TestDbContext>(Configuration(settings), configure);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static Dictionary<string, string?> Settings(string password = "K33pTr4ck")
        => new()
        {
            ["Database:Host"] = "db.local",
            ["Database:Port"] = "5433",
            ["Database:Database"] = "ShardDb",
            ["Database:Username"] = "ShardUser",
            ["Database:Password"] = password,
        };

    private static List<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor> Interceptors(DbContext context)
        => [.. context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.Interceptors ?? []];
}
