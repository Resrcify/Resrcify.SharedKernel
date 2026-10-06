using System;
using System.Globalization;

namespace Resrcify.SharedKernel.MessageBus.Configuration;

/// <summary>Where the message bus connects.</summary>
public sealed record RabbitMqConnection(
    string Host,
    int Port,
    string Username,
    string Password)
{
    /// <summary>An <c>amqp://</c> URI with the credentials escaped.</summary>
    public string ConnectionString
        => string.Create(
            CultureInfo.InvariantCulture,
            $"amqp://{Uri.EscapeDataString(Username)}:{Uri.EscapeDataString(Password)}@{Host}:{Port}");
}
