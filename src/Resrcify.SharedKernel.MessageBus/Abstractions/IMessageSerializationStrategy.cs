using System.Text.Json;
using Resrcify.SharedKernel.MessageBus.Serialization;

namespace Resrcify.SharedKernel.MessageBus.Abstractions;

/// <summary>
/// Customises the JSON every bus of this service writes and reads (its own bus, the scatter-gather reply
/// bus and each rate-limited queue). It gets a copy of the defaults (System.Text.Json web defaults), once
/// per service, and can change anything on it: converters, naming, or which members are written at all.
/// Set it with <c>MessageBusBuilder.UseSerializationStrategy</c>; the default,
/// <see cref="DefaultMessageSerializationStrategy"/>, changes nothing.
/// </summary>
/// <remarks>
/// The receiver's class doesn't limit what goes over the wire: the sender writes every member of the object
/// it sends, and the receiver only drops what it has no property for. Keep data off the wire here, on the
/// sending side, e.g. with a <c>DefaultJsonTypeInfoResolver</c> modifier that removes members.
/// </remarks>
public interface IMessageSerializationStrategy
{
    void Configure(JsonSerializerOptions options);
}
