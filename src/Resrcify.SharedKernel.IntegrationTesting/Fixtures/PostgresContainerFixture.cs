using System;
using System.Data.Common;
using System.Globalization;
using System.Threading.Tasks;
using DotNet.Testcontainers.Networks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Resrcify.SharedKernel.IntegrationTesting.Fixtures;

/// <summary>
/// Postgres-flavoured <see cref="ContainerFixture{TContainer}"/>, on the image production runs
/// (<see cref="DefaultImage"/>). The database, credentials and image are set with init properties, without a
/// subclass: <c>new PostgresContainerFixture { Database = "ShardDb", Username = "ShardUser", Password = "..." }</c>.
/// A subclass can still override <see cref="Configure"/> for any other builder option.
///
/// Exposes <see cref="Host"/> and <see cref="Port"/> so consumers can wire
/// connection settings without poking at <see cref="ContainerFixture{TContainer}.Container"/>
/// directly. <see cref="Port"/> is the host-side mapped port for Postgres'
/// fixed in-container <see cref="ContainerPort"/> (5432).
/// </summary>
public class PostgresContainerFixture
    : ContainerFixture<PostgreSqlContainer>
{
    private const ushort ContainerPort = 5432;

    // Waits on a real container, so on real time.
    private static readonly TimeProvider Time = TimeProvider.System;

    /// <summary>
    /// The image unless <see cref="Image"/> says otherwise: the version production runs (Resrcify.Kubernetes' database
    /// stateful sets).
    /// </summary>
    protected const string DefaultImage = "postgres:18";

    /// <summary>
    /// Environment variable naming the image to run instead, for every fixture (it wins over <see cref="Image"/> and a
    /// <c>WithImage</c> in <see cref="Configure"/>), e.g. to try a whole suite against the next version.
    /// </summary>
    public const string ImageVariable = "RESRCIFY_TEST_POSTGRES_IMAGE";

    /// <summary>
    /// The image to run: <see cref="DefaultImage"/> (production's) unless set. Keep it: a test on another version than
    /// production's proves less; <see cref="ImageVariable"/> tries a suite on another one without a code change.
    /// </summary>
    public string Image { get; init; } = DefaultImage;

    /// <summary>The database the container creates (Testcontainers' <c>postgres</c> unless set).</summary>
    public string? Database { get; init; }

    /// <summary>The user the container creates (Testcontainers' <c>postgres</c> unless set).</summary>
    public string? Username { get; init; }

    /// <summary>The user's password (Testcontainers' <c>postgres</c> unless set).</summary>
    public string? Password { get; init; }

    /// <summary>Any other builder option, for a subclass; runs after <see cref="Image"/> and the credentials are applied.</summary>
    protected virtual PostgreSqlBuilder Configure(PostgreSqlBuilder builder)
        => builder;

    private INetwork? _network;
    private string[] _networkAliases = [];

    /// <summary>
    /// Puts the container on <paramref name="network"/> as <paramref name="aliases"/>, so other containers on it reach
    /// this one by name; the test process keeps using <see cref="Host"/> and the mapped port. Call it before the fixture
    /// starts. Without it the container is on the default network only.
    /// </summary>
    public void JoinNetwork(
        INetwork network,
        params string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(aliases);
        _network = network;
        _networkAliases = aliases;
    }

    protected override PostgreSqlContainer Build()
    {
        var builder = new PostgreSqlBuilder(Image);
        if (Database is { } database)
            builder = builder.WithDatabase(database);
        if (Username is { } username)
            builder = builder.WithUsername(username);
        if (Password is { } password)
            builder = builder.WithPassword(password);
        builder = Configure(builder);
        if (_network is { } network)
            builder = builder
                .WithNetwork(network)
                .WithNetworkAliases(_networkAliases);
        return ContainerImages.FromEnvironment(ImageVariable) is { } image
            ? builder.WithImage(image).Build()
            : builder.Build();
    }

    public string Host
        => Container.Hostname;

    public string Port
        => Container.GetMappedPublicPort(ContainerPort).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The postgres image boots twice (init scripts on a temporary server, then the
    /// real server), so a passing wait strategy can still race the real listener on
    /// a cold or busy host. This probes a real <c>SELECT 1</c> until it succeeds.
    /// </summary>
    protected override async Task OnStartedAsync()
    {
        var deadline = Time.GetUtcNow() + TimeSpan.FromSeconds(30);
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(
                    Container.GetConnectionString());
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync();
                return;
            }
            catch (NpgsqlException) when (Time.GetUtcNow() < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), Time);
            }
        }
    }

    /// <summary>
    /// Returns a connection string for the container that targets a fresh,
    /// uniquely-named database. The database does not exist yet — EF Core creates
    /// it (and its schema) on the first <c>EnsureCreated</c> / <c>Migrate</c> call.
    /// Use one per test class (or per test) to get full isolation without sharing
    /// state or truncating tables between runs.
    /// </summary>
    public string CreateIsolatedConnectionString()
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = Container.GetConnectionString(),
        };
        builder["Database"] = $"test_{Guid.NewGuid():N}";
        return builder.ConnectionString;
    }
}
