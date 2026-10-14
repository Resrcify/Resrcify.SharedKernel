namespace Resrcify.SharedKernel.MessageBus.Configuration;

/// <summary>
/// Where the message bus connects, as configured: the <c>MessageBus</c> section every service already has (<c>Host</c>,
/// <c>Port</c>, <c>Username</c>, <c>Password</c>; in Kubernetes <c>MessageBus__Host</c> and so on), so a service switches
/// to <see cref="MessageBusBuilder.UseRabbitMqFromConfiguration"/> without a configuration change. Validated when the host
/// starts.
/// </summary>
/// <remarks>
/// Registered as the default <c>IOptions&lt;RabbitMqOptions&gt;</c>, so a service can read it too (e.g. for a connection of
/// its own); <see cref="ToConnection"/> makes the <see cref="RabbitMqConnection"/> the bus uses. A string in
/// configuration binds to <see cref="Port"/> and <see cref="UseTls"/> (<c>"5672"</c>, <c>"true"</c>).
/// </remarks>
public sealed class RabbitMqOptions
{
    /// <summary>The section the services keep their message bus settings in.</summary>
    public const string SectionName = "MessageBus";

    /// <summary>The broker's host name or address. Required.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The broker's AMQP port; 5672 by default (RabbitMQ listens on 5671 for TLS).</summary>
    public int Port { get; set; } = 5672;

    /// <summary>The user to connect as. Required.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>The user's password. Required.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>The virtual host; <c>/</c>, RabbitMQ's default, when not set.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Connect over TLS (<c>amqps://</c>; see <see cref="RabbitMqConnection.UseTls"/>).</summary>
    public bool UseTls { get; set; }

    /// <summary>The connection these settings describe (which prints without the password).</summary>
    public RabbitMqConnection ToConnection()
        => new(Host, Port, Username, Password)
        {
            VirtualHost = string.IsNullOrWhiteSpace(VirtualHost) ? "/" : VirtualHost,
            UseTls = UseTls,
        };
}
