using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Testcontainers.RabbitMq;

namespace Resrcify.SharedKernel.IntegrationTesting.Fixtures;

/// <summary>
/// RabbitMQ-flavoured <see cref="ContainerFixture{TContainer}"/>. Subclasses
/// (or direct consumers) override <see cref="Configure"/> to set image,
/// credentials, and any other builder options. Adds a polling helper that
/// asserts a named exchange has received at least one publish via the RMQ
/// management HTTP API.
///
/// The management port (15672) is exposed automatically so
/// <see cref="WaitForExchangePublish"/> works without consumers having to
/// add <c>.WithPortBinding(15672, true)</c> in their <see cref="Configure"/>
/// override.
/// </summary>
public class RabbitMqContainerFixture
    : ContainerFixture<RabbitMqContainer>
{
    private const ushort AmqpContainerPort = 5672;
    private const ushort ManagementContainerPort = 15672;

    protected virtual RabbitMqBuilder Configure(RabbitMqBuilder builder)
        => builder;

    protected override RabbitMqContainer Build()
        => Configure(new RabbitMqBuilder())
            .WithPortBinding(ManagementContainerPort, assignRandomHostPort: true)
            .Build();

    public string Host
        => Container.Hostname;

    public string AmqpPort
        => Container.GetMappedPublicPort(AmqpContainerPort).ToString(CultureInfo.InvariantCulture);

    public string ManagementPort
        => Container.GetMappedPublicPort(ManagementContainerPort).ToString(CultureInfo.InvariantCulture);

    public Uri ManagementUrl
        => new($"http://{Host}:{ManagementPort}");

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
        var (user, pass) = ParseCredentials(Container.GetConnectionString());

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
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
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

            await Task.Delay(interval, cancellationToken);
        }

        return false;
    }

    private static (string User, string Pass) ParseCredentials(string connectionString)
    {
        var uri = new Uri(connectionString);
        var parts = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(parts[0]);
        var pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        return (user, pass);
    }
}
