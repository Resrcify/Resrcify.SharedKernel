using Rebus.Config;
using Resrcify.SharedKernel.MessageBus.Abstractions;

namespace Resrcify.SharedKernel.MessageBus.Configuration;

/// <summary>Leaves every bus on the package defaults.</summary>
public sealed class DefaultBusConfigurationStrategy : IBusConfigurationStrategy
{
    public static DefaultBusConfigurationStrategy Instance { get; } = new();

    public void ConfigureTransport(RabbitMqOptionsBuilder transport)
    {
        // The defaults are already applied; nothing to change.
    }

    public void ConfigureOptions(OptionsConfigurer options)
    {
        // The defaults are already applied; nothing to change.
    }
}
