using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rebus.Transport.InMem;
using Resrcify.SharedKernel.MessageBus.Configuration;
using Resrcify.SharedKernel.MessageBus.Extensions;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Support;

/// <summary>Starts services on one in-memory network, as separate services would talk over RabbitMQ.</summary>
internal static class InMemoryServices
{
    public static async Task<IHost> StartAsync(
        InMemNetwork network,
        Action<MessageBusBuilder> configure,
        Action<IServiceCollection>? services = null)
    {
        var builder = Host.CreateApplicationBuilder();
        services?.Invoke(builder.Services);
        builder.Services.AddMessageBus(bus => configure(bus.UseInMemory(network)));
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    /// <summary>Waits for <paramref name="condition"/>, failing after five seconds.</summary>
    public static Task WaitUntilAsync(Func<bool> condition)
        => WaitUntilAsync(condition, TimeSpan.FromSeconds(5));

    /// <summary>Waits for <paramref name="condition"/>, failing after <paramref name="timeout"/>.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var giveUpAt = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > giveUpAt)
                throw new TimeoutException($"The condition was not met within {timeout.TotalSeconds} s.");
            await Task.Delay(20);
        }
    }
}
