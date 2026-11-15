using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Rebus.Transport.InMem;

namespace Resrcify.SharedKernel.MessageBus.Configuration;

/// <summary>
/// The transport <see cref="MessageBusBuilder.UseTransportFromConfiguration"/> chose, and why:
/// <list type="bullet">
/// <item>a <c>MessageBus</c> section (any key under it): RabbitMQ, in every environment, the section validated on start;</item>
/// <item>no section, in Production: RabbitMQ all the same, so the start fails naming the key
/// (<c>MessageBus:Host is required.</c>) rather than an instance whose messages never leave it;</item>
/// <item>no section anywhere else (a local run, Development, a test): in memory, needing no broker.</item>
/// </list>
/// </summary>
/// <param name="HasSection">Whether the host's configuration has a <c>MessageBus</c> section.</param>
/// <param name="Environment">The host's environment, for the start-up log.</param>
/// <param name="Connection">The connection given to the bus, when it chose RabbitMQ.</param>
/// <param name="Network">The in-memory network given to the bus, when it chose memory.</param>
internal sealed record TransportSelection(
    bool HasSection,
    IHostEnvironment Environment,
    Func<IServiceProvider, RabbitMqConnection>? Connection,
    InMemNetwork? Network)
{
    public bool IsRabbitMq
        => Connection is not null;

    /// <summary>Whether <paramref name="configuration"/> and <paramref name="environment"/> call for RabbitMQ.</summary>
    public static bool CallsForRabbitMq(
        IConfiguration configuration,
        IHostEnvironment environment)
        => HasSectionIn(configuration) || environment.IsProduction();

    /// <summary>Whether <paramref name="configuration"/> has a <c>MessageBus</c> section: any key under it.</summary>
    public static bool HasSectionIn(IConfiguration configuration)
        => configuration
            .GetSection(RabbitMqOptions.SectionName)
            .Exists();

    /// <summary>
    /// Whether the bus still runs on this transport: not when it was given another one afterwards (a later
    /// <c>UseRabbitMq</c> or <c>UseInMemory</c>, or the test harness, which switches every bus to its own network).
    /// </summary>
    public bool IsInUseBy(MessageBusSettings settings)
        => IsRabbitMq
            ? ReferenceEquals(settings.Connection, Connection)
            : ReferenceEquals(settings.InMemoryNetwork, Network);
}
