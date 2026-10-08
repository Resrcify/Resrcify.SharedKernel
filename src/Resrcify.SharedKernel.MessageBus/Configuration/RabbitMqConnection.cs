using System;
using System.Globalization;
using System.Text;

namespace Resrcify.SharedKernel.MessageBus.Configuration;

/// <summary>Where the message bus connects: every connection it makes (each bus', the broker watcher's) uses it.</summary>
public sealed record RabbitMqConnection(
    string Host,
    int Port,
    string Username,
    string Password)
{
    /// <summary>The virtual host (<c>/</c>, RabbitMQ's default, when not set).</summary>
    public string VirtualHost { get; init; } = "/";

    /// <summary>Connect over TLS (<c>amqps://</c>; RabbitMQ listens on 5671 for it). Certificates and the like go in
    /// the configuration strategy (<c>ConfigureTransport</c> and <c>ConfigureConnectionFactory</c>).</summary>
    public bool UseTls { get; init; }

    /// <summary>An <c>amqp://</c> (or <c>amqps://</c>) URI with the credentials and the virtual host escaped.</summary>
    public string ConnectionString
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{(UseTls ? "amqps" : "amqp")}://{Uri.EscapeDataString(Username)}:{Uri.EscapeDataString(Password)}@{Host}:{Port}/{Uri.EscapeDataString(VirtualHost)}");

    // Printed (ToString, a log, an exception message) without the password: a record prints every property, the
    // password and the connection string holding it included.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"Host = {Host}, Port = {Port}, Username = {Username}, Password = ***, ");
        builder.Append(CultureInfo.InvariantCulture, $"VirtualHost = {VirtualHost}, UseTls = {UseTls}");
        return true;
    }
}
