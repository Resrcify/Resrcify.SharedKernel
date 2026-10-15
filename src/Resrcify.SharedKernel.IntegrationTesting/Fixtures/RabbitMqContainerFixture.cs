using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Networks;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Testcontainers.RabbitMq;

namespace Resrcify.SharedKernel.IntegrationTesting.Fixtures;

/// <summary>
/// RabbitMQ-flavoured <see cref="ContainerFixture{TContainer}"/>, on the image production runs
/// (<see cref="DefaultImage"/>). The credentials, the image and production's queue defaults
/// (<see cref="ProductionQueueDefaults"/>) are set with init properties, without a subclass:
/// <c>new RabbitMqContainerFixture { Username = "messageUser", Password = "...", ProductionQueueDefaults = true }</c>.
/// A subclass can still override <see cref="Configure"/> for any other builder option.
/// <para>
/// It reads the broker back: <see cref="QueuesAsync"/> lists the queues with their types,
/// <see cref="AddQuorumVirtualHostAsync"/> makes a virtual host whose queues are quorum queues, as on production,
/// <see cref="WaitForExchangePublish"/> waits for an exchange's first publish (through the management API, whose port
/// is exposed), and <see cref="Connection"/> is the broker as the message bus connects to it.
/// </para>
/// </summary>
public class RabbitMqContainerFixture
    : ContainerFixture<RabbitMqContainer>
{
    private const ushort AmqpContainerPort = 5672;
    private const ushort ManagementContainerPort = 15672;

    // Waits on a real container, so on real time.
    private static readonly TimeProvider Time = TimeProvider.System;

    /// <summary>
    /// The image unless <see cref="Image"/> says otherwise: exactly what production runs, the RabbitMQ Cluster
    /// Operator's default image (v2.23.0's <c>defaultRabbitmqImage</c>), with the management plugin
    /// (<see cref="ManagementUrl"/> needs it). Move it with the operator: each 4.x refuses more deprecated features
    /// (4.3 refuses transient non-exclusive queues).
    /// </summary>
    protected const string DefaultImage = "rabbitmq:4.3.4-management";

    /// <summary>
    /// Environment variable naming the image to run instead, for every fixture (it wins over <see cref="Image"/> and a
    /// <c>WithImage</c> in <see cref="Configure"/>), e.g. to try a whole suite against the next version.
    /// </summary>
    public const string ImageVariable = "RESRCIFY_TEST_RABBITMQ_IMAGE";

    /// <summary>
    /// Production's queue defaults (Resrcify.Kubernetes' <c>RabbitmqCluster</c> <c>additionalConfig</c>), which
    /// <see cref="ProductionQueueDefaults"/> gives the broker: a queue declared without a type is a quorum queue, and
    /// every queue gets a delivery limit of a million (RabbitMQ 4 limits a quorum queue to 20 redeliveries otherwise).
    /// </summary>
    public const string ProductionQueueConfig = """
        default_queue_type = quorum
        quorum_queue.initial_cluster_size = 1
        default_policies.operator.delivery.vhost_pattern = .*
        default_policies.operator.delivery.queue_pattern = .*
        default_policies.operator.delivery.delivery_limit = 1000000
        """;

    /// <summary>The delivery limit production's operator policy gives every queue.</summary>
    public const int ProductionDeliveryLimit = 1_000_000;

    /// <summary>
    /// The image to run: <see cref="DefaultImage"/> (production's) unless set. Keep it: a test on another version than
    /// production's proves less; <see cref="ImageVariable"/> tries a suite on another one without a code change.
    /// </summary>
    public string Image { get; init; } = DefaultImage;

    /// <summary>The user the broker creates (Testcontainers' <c>rabbitmq</c> unless set).</summary>
    public string? Username { get; init; }

    /// <summary>The user's password (Testcontainers' <c>rabbitmq</c> unless set).</summary>
    public string? Password { get; init; }

    /// <summary>
    /// Runs the broker with production's queue defaults (<see cref="ProductionQueueConfig"/>): every virtual host's
    /// queues are quorum queues unless declared otherwise, with production's delivery limit. Off by default, the
    /// broker's own defaults (classic queues).
    /// </summary>
    public bool ProductionQueueDefaults { get; init; }

    /// <summary>Any other builder option, for a subclass; runs after the properties are applied.</summary>
    protected virtual RabbitMqBuilder Configure(RabbitMqBuilder builder)
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

    protected override RabbitMqContainer Build()
    {
        var builder = new RabbitMqBuilder(Image);
        if (Username is { } username)
            builder = builder.WithUsername(username);
        if (Password is { } password)
            builder = builder.WithPassword(password);
        if (ProductionQueueDefaults)
            builder = builder.WithResourceMapping(
                Encoding.UTF8.GetBytes(ProductionQueueConfig + "\n"),
                "/etc/rabbitmq/conf.d/90-production-queues.conf");
        builder = Configure(builder)
            .WithPortBinding(ManagementContainerPort, assignRandomHostPort: true);
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

    public string AmqpPort
        => Container.GetMappedPublicPort(AmqpContainerPort).ToString(CultureInfo.InvariantCulture);

    public string ManagementPort
        => Container.GetMappedPublicPort(ManagementContainerPort).ToString(CultureInfo.InvariantCulture);

    public Uri ManagementUrl
        => new($"http://{Host}:{ManagementPort}");

    /// <summary>The broker's user, as the container was started with it.</summary>
    public string BrokerUsername
        => Credentials().User;

    /// <summary>The broker user's password, as the container was started with it.</summary>
    public string BrokerPassword
        => Credentials().Pass;

    /// <summary>The broker as the test process reaches it, in the default virtual host (<c>/</c>), for the message bus.</summary>
    public RabbitMqConnection Connection
        => new(
            Host,
            int.Parse(AmqpPort, CultureInfo.InvariantCulture),
            BrokerUsername,
            BrokerPassword);

    /// <summary>
    /// Adds a virtual host of its own (named <paramref name="name"/>, or a new unique one) whose queues are quorum
    /// queues unless declared otherwise, with production's delivery limit, and lets the broker's user use it: a
    /// production-like corner of a shared broker, for one test.
    /// </summary>
    /// <returns><see cref="Connection"/> in that virtual host.</returns>
    public async Task<RabbitMqConnection> AddQuorumVirtualHostAsync(
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        var virtualHost = name ?? $"quorum-{Guid.NewGuid():N}";
        await RabbitMqCtlAsync(cancellationToken, "add_vhost", virtualHost, "--default-queue-type", "quorum");
        await RabbitMqCtlAsync(cancellationToken, "set_permissions", "-p", virtualHost, BrokerUsername, ".*", ".*", ".*");
        await RabbitMqCtlAsync(
            cancellationToken,
            "set_operator_policy",
            "-p",
            virtualHost,
            "delivery",
            ".*",
            $$"""{"delivery-limit":{{ProductionDeliveryLimit}}}""",
            "--apply-to",
            "queues");
        return Connection with { VirtualHost = virtualHost };
    }

    /// <summary>Every queue of <paramref name="virtualHost"/>, with its type (<c>classic</c>, <c>quorum</c>) and whether it is exclusive.</summary>
    public async Task<IReadOnlyList<RabbitMqQueue>> QueuesAsync(
        string virtualHost = "/",
        CancellationToken cancellationToken = default)
    {
        var listed = await RabbitMqCtlAsync(
            cancellationToken,
            "list_queues",
            "-p",
            virtualHost,
            "--quiet",
            "--no-table-headers",
            "name",
            "type",
            "exclusive");
        return [.. listed
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(columns => columns.Length == 3)
            // A quorum queue can't be exclusive: its column is empty.
            .Select(columns => new RabbitMqQueue(
                columns[0],
                columns[1],
                string.Equals(columns[2], "true", StringComparison.Ordinal)))];
    }

    /// <summary>
    /// Polls the RMQ management API for the named exchange and returns true
    /// once <c>message_stats.publish_in</c> reports at least one published
    /// message. Returns false if the deadline elapses.
    /// </summary>
    public async Task<bool> WaitForExchangePublish(
        string exchange,
        TimeSpan timeout,
        string vhost = "/",
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        var (user, pass) = Credentials();

        using var http = new HttpClient
        {
            BaseAddress = ManagementUrl,
            Timeout = TimeSpan.FromSeconds(5),
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}")));

        var path = new Uri(
            $"/api/exchanges/{Uri.EscapeDataString(vhost)}/{Uri.EscapeDataString(exchange)}",
            UriKind.Relative);
        var deadline = Time.GetUtcNow() + timeout;

        while (Time.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var resp = await http.GetAsync(path, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                    if (doc.RootElement.TryGetProperty("message_stats", out var stats)
                        && stats.TryGetProperty("publish_in", out var publishIn)
                        && publishIn.GetInt64() > 0)
                        return true;
                }
            }
            catch (HttpRequestException)
            {
                // RMQ management plugin may still be warming up — keep polling.
            }

            await Task.Delay(interval, Time, cancellationToken);
        }

        return false;
    }

    private (string User, string Pass) Credentials()
    {
        var uri = new Uri(Container.GetConnectionString());
        var parts = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(parts[0]);
        var pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        return (user, pass);
    }

    private async Task<string> RabbitMqCtlAsync(
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var result = await Container.ExecAsync(["rabbitmqctl", .. arguments], cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"rabbitmqctl {string.Join(' ', arguments)} failed: {result.Stderr}");
        return result.Stdout;
    }
}

/// <summary>A queue on the broker, as <see cref="RabbitMqContainerFixture.QueuesAsync"/> lists it.</summary>
/// <param name="Name">The queue's name.</param>
/// <param name="Type">Its type: <c>classic</c>, <c>quorum</c> or <c>stream</c>.</param>
/// <param name="Exclusive">Whether it belongs to one connection (never a quorum queue).</param>
public sealed record RabbitMqQueue(
    string Name,
    string Type,
    bool Exclusive);
