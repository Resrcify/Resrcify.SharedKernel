using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;
namespace Resrcify.SharedKernel.UnitOfWork.Converters;

public class DomainEventConverter : JsonConverter<IDomainEvent>
{
    public override IDomainEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Parse the JSON document
        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        var root = jsonDoc.RootElement;

        // Extract the `$type` metadata
        if (!root.TryGetProperty("$type", out var typeProperty))
        {
            throw new JsonException("The JSON does not contain a $type property.");
        }

        var typeName = typeProperty.GetString();
        if (string.IsNullOrEmpty(typeName))
        {
            throw new JsonException("$type property is empty or null.");
        }

        // Resolve the type
        var eventType = Type.GetType(typeName)
            ?? throw new InvalidOperationException($"Type '{typeName}' could not be resolved.");

        // Deserialize to the resolved type
        return (IDomainEvent?)JsonSerializer.Deserialize(root.GetRawText(), eventType, options);
    }

    public override void Write(Utf8JsonWriter writer, IDomainEvent value, JsonSerializerOptions options)
    {
        // Serialize the object with type metadata
        var typeName = value.GetType().AssemblyQualifiedName;

        // Convert object to JSON
        using var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(value, value.GetType(), options));
        var jsonObj = jsonDoc.RootElement.Clone();

        // Create a new JSON object with `$type` included
        writer.WriteStartObject();
        writer.WriteString("$type", typeName);

        foreach (var property in jsonObj.EnumerateObject())
        {
            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}