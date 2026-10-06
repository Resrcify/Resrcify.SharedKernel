using Rebus.Config;
using Resrcify.SharedKernel.MessageBus.Configuration;

namespace Resrcify.SharedKernel.MessageBus.Abstractions;

/// <summary>
/// Customises Rebus for every bus a service runs: its own bus, the scatter-gather reply bus and each
/// rate-limited queue. Runs after the defaults, so it can change any of them (e.g. retries, the error
/// queue, publisher confirms, connection-factory settings such as TLS). Set it with <c>MessageBusBuilder.UseConfigurationStrategy</c>; the
/// default, <see cref="DefaultBusConfigurationStrategy"/>, changes nothing.
/// </summary>
public interface IBusConfigurationStrategy
{
    /// <summary>The RabbitMQ transport of each bus, e.g. <c>transport.SetPublisherConfirms(true)</c>.</summary>
    void ConfigureTransport(RabbitMqOptionsBuilder transport);

    /// <summary>The Rebus options of each bus, e.g. <c>options.RetryStrategy(maxDeliveryAttempts: 10)</c>.</summary>
    void ConfigureOptions(OptionsConfigurer options);
}
