using System.Text.Json;

namespace Resrcify.SharedKernel.MessageBus.Serialization;

/// <summary>
/// The JSON format on the wire, shared by every bus a service runs (its own, the scatter-gather reply
/// bus and each rate-limited queue), so they can't drift apart. Web defaults: camelCase, case-insensitive reads.
/// </summary>
internal static class MessageJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
