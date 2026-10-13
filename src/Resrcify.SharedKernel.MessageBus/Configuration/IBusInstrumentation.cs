using Rebus.Config;

namespace Resrcify.SharedKernel.MessageBus.Configuration;

/// <summary>
/// Hooks into every bus the service builds (its own, the scatter-gather reply bus, each rate-limited queue's), after
/// the package's own options. Registered by the test harness (Resrcify.SharedKernel.MessageBus.Testing).
/// </summary>
internal interface IBusInstrumentation
{
    void Configure(OptionsConfigurer options);
}
