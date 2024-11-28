using System;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Resrcify.SharedKernel.UnitOfWork.Converters;

public class PolymorphicJsonConverter<T> : JsonConverter<T>
{
    private const string TypePropertyName = "$type";
    private const string DataPropertyName = "$data";

    public override T? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        var rootElement = jsonDoc.RootElement;

        if (!rootElement.TryGetProperty(TypePropertyName, out var typeProperty))
        {
            throw new JsonException($"Missing '{TypePropertyName}' property.");
        }

        var typeName = typeProperty.GetString();
        if (string.IsNullOrEmpty(typeName))
            throw new JsonException($"Type name cannot be null or empty.");


        var targetType = Type.GetType(typeName)
            ?? throw new JsonException($"Unknown type: {typeName}");


        if (!rootElement.TryGetProperty(DataPropertyName, out var dataProperty))
            throw new JsonException($"Missing '{DataPropertyName}' property.");


        var data = dataProperty.GetRawText();
        if (string.IsNullOrEmpty(data))
            throw new JsonException($"Data for type '{typeName}' cannot be null or empty.");


        return (T?)JsonSerializer.Deserialize(data, targetType, options);
    }

    public override void Write(
         Utf8JsonWriter writer,
         T value,
         JsonSerializerOptions options)
    {
        if (value == null)
            throw new JsonException($"Cannot serialize a null value of type {typeof(T)}.");

        writer.WriteStartObject();
        writer.WriteString(TypePropertyName, value.GetType().AssemblyQualifiedName);
        writer.WritePropertyName(DataPropertyName);
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
        writer.WriteEndObject();
    }
}