using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;

/// <summary>
/// A requesting service the way Tournament and Oracle are: no database, no input queue, asking directly
/// (<see cref="IScatterGatherClient"/>), and connected through the <c>MessageBus</c> section of its configuration
/// (<c>UseRabbitMqFromConfiguration</c>), which this host is given as <c>ServiceHostFixture</c> gives a service's.
/// </summary>
internal sealed class ConfiguredRequesterHost : IAsyncDisposable
{
    private readonly IHost _host;

    private ConfiguredRequesterHost(IHost host)
        => _host = host;

    /// <summary>The reply queue the scatter-gather transport declared.</summary>
    public string ReplyQueue
        => _host.Services.GetRequiredService<ScatterGatherTransport>().ReplyQueue
            ?? throw new InvalidOperationException("The scatter-gather transport hasn't started.");

    /// <summary>The <c>MessageBus:*</c> settings for <paramref name="connection"/>, as a service's configuration has them.</summary>
    public static Dictionary<string, string?> SettingsFor(RabbitMqConnection connection)
        => new()
        {
            ["MessageBus:Host"] = connection.Host,
            ["MessageBus:Port"] = connection.Port.ToString(CultureInfo.InvariantCulture),
            ["MessageBus:Username"] = connection.Username,
            ["MessageBus:Password"] = connection.Password,
            ["MessageBus:VirtualHost"] = connection.VirtualHost,
        };

    public static async Task<ConfiguredRequesterHost> StartAsync(
        Dictionary<string, string?> settings,
        string requestQueue,
        Action<MessageBusBuilder>? configureBus = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddMessageBus(bus =>
        {
            bus.UseRabbitMqFromConfiguration()
                .AddMessage<PingRequest>(WireNames.PingRequest, sendTo: requestQueue)
                .AddMessage<PingResponse>(WireNames.PingResponse)
                .AddScatterGather();
            configureBus?.Invoke(bus);
        });

        var host = builder.Build();
        try
        {
            await host.StartAsync();
        }
        catch
        {
            host.Dispose();
            throw;
        }
        return new ConfiguredRequesterHost(host);
    }

    public Task<Result<PingResponse>> RequestAsync(PingRequest request, TimeSpan timeout)
        => _host.Services.GetRequiredService<IScatterGatherClient>().RequestAsync<PingRequest, PingResponse>(request, timeout);

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
