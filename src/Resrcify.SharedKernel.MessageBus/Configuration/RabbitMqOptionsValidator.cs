using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Resrcify.SharedKernel.MessageBus.Configuration;

/// <summary>
/// Checks the bus' <see cref="RabbitMqOptions"/> when the host starts, naming the keys that are wrong. Only while the
/// bus still connects with them: a bus switched to memory (<c>UseInMemory</c>, the test harness) or given another
/// connection afterwards reads no configuration, so its settings may be missing.
/// </summary>
/// <param name="settings">The bus' settings, to see whether it still connects with these options.</param>
/// <param name="connection">The connection <c>UseRabbitMqFromConfiguration</c> gave the bus.</param>
/// <param name="sectionPath">The section the options are bound from, for the messages (e.g. <c>MessageBus</c>).</param>
internal sealed class RabbitMqOptionsValidator(
    MessageBusSettings settings,
    Func<IServiceProvider, RabbitMqConnection> connection,
    string sectionPath)
    : IValidateOptions<RabbitMqOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        RabbitMqOptions options)
    {
        if (name != Options.DefaultName || !ReferenceEquals(settings.Connection, connection))
            return ValidateOptionsResult.Skip;

        var failures = new List<string>();
        Require(failures, nameof(RabbitMqOptions.Host), options.Host);
        Require(failures, nameof(RabbitMqOptions.Username), options.Username);
        Require(failures, nameof(RabbitMqOptions.Password), options.Password);
        if (options.Port is < 1 or > 65535)
            failures.Add($"{sectionPath}:{nameof(RabbitMqOptions.Port)} must be a port (1-65535); it is {options.Port}.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private void Require(
        List<string> failures,
        string key,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            failures.Add($"{sectionPath}:{key} is required.");
    }
}
