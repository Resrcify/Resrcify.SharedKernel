using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
namespace Resrcify.SharedKernel.UnitOfWork.Converters;

/// <summary>
/// Writes a domain event as its concrete type's JSON with the type's assembly-qualified name first, in a
/// <c>$type</c> property, and reads it back as that type.
/// </summary>
/// <remarks>
/// The format is <c>{"$type":"&lt;assembly-qualified name&gt;", ...the event's own properties}</c>; rows already in an
/// outbox keep reading. Type names are resolved once per name (a name that resolves to nothing is remembered too).
/// </remarks>
public class DomainEventConverter
    : JsonConverter<IDomainEvent>
{
    private const string TypeProperty = "$type";

    private static readonly JsonEncodedText EncodedTypeProperty = JsonEncodedText.Encode(TypeProperty);
    private static readonly ConcurrentDictionary<string, Type?> ResolvedTypes = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Type, TypeName> TypeNames = new();

    public override IDomainEvent? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        // The converter writes $type first: read it ahead on a copy, then deserialize straight from the reader.
        var lookahead = reader;
        if (TryReadLeadingTypeName(ref lookahead, out var leadingTypeName))
            return (IDomainEvent?)JsonSerializer.Deserialize(
                ref reader,
                Resolve(leadingTypeName),
                options);

        // $type is somewhere else (JSON not written by this converter): find it in the parsed object.
        using var jsonDoc = JsonDocument.ParseValue(
            ref reader);
        var root = jsonDoc.RootElement;

        if (!root.TryGetProperty(
            TypeProperty,
            out var typeProperty))
        {
            throw new JsonException("The JSON does not contain a $type property.");
        }

        return (IDomainEvent?)root.Deserialize(
            Resolve(typeProperty.GetString()),
            options);
    }

    public override void Write(
        Utf8JsonWriter writer,
        IDomainEvent value,
        JsonSerializerOptions options)
    {
        var eventType = value.GetType();
        var typeName = TypeNames.GetOrAdd(
            eventType,
            type => new TypeName(type.AssemblyQualifiedName!));

        if (writer.Options.Indented || writer.Options.Encoder != options.Encoder)
        {
            WriteMemberByMember(
                writer,
                value,
                eventType,
                typeName.Name,
                options);
            return;
        }

        // Unindented, with the writer escaping as the serializer does: the event's own JSON is already what the
        // writer would write member by member, so it is copied once, after $type.
        var members = JsonSerializer.SerializeToUtf8Bytes(
            value,
            eventType,
            options);
        writer.WriteRawValue(
            WithTypeFirst(members, typeName, options),
            skipInputValidation: true);
    }

    /// <summary>
    /// Reads <c>"$type":"..."</c> when it is the object's first property; <see langword="false"/> for anything else.
    /// </summary>
    private static bool TryReadLeadingTypeName(
        ref Utf8JsonReader reader,
        out string? typeName)
    {
        typeName = null;
        if (reader.TokenType != JsonTokenType.StartObject
            || !reader.Read()
            || reader.TokenType != JsonTokenType.PropertyName
            || !reader.ValueTextEquals(TypeProperty)
            || !reader.Read()
            || reader.TokenType != JsonTokenType.String)
        {
            return false;
        }

        typeName = reader.GetString();
        return true;
    }

    private static Type Resolve(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            throw new JsonException("$type property is empty or null.");

        return ResolvedTypes.GetOrAdd(
                typeName,
                name => Type.GetType(name))
            ?? throw new InvalidOperationException(
                $"Type '{typeName}' could not be resolved.");
    }

    /// <summary>The event's JSON object with <c>"$type":"..."</c> put in front of its own properties.</summary>
    private static byte[] WithTypeFirst(
        byte[] members,
        TypeName typeName,
        JsonSerializerOptions options)
    {
        if (members.Length < 2 || members[0] != (byte)'{' || members[^1] != (byte)'}')
            throw new JsonException($"A domain event must serialize to a JSON object; '{typeName.Name}' did not.");

        // Escaped as the writer escapes (the encoder is the serializer's: see Write).
        var name = options.Encoder is null
            ? EncodedTypeProperty.EncodedUtf8Bytes
            : JsonEncodedText.Encode(TypeProperty, options.Encoder).EncodedUtf8Bytes;
        var type = options.Encoder is null
            ? typeName.Encoded.EncodedUtf8Bytes
            : JsonEncodedText.Encode(typeName.Name, options.Encoder).EncodedUtf8Bytes;
        var hasMembers = members.Length > 2;

        // {"<name>":"<type>" and then "," + the members after their "{" (as many bytes as the members), or "}".
        var tail = hasMembers ? members.Length : 1;
        var json = new byte[1 + name.Length + 2 + 1 + type.Length + 2 + tail];
        var position = 0;
        json[position++] = (byte)'{';
        position = WriteQuoted(json, position, name);
        json[position++] = (byte)':';
        position = WriteQuoted(json, position, type);
        if (hasMembers)
        {
            json[position++] = (byte)',';
            members.AsSpan(1).CopyTo(json.AsSpan(position));
        }
        else
        {
            json[position] = (byte)'}';
        }

        return json;
    }

    private static int WriteQuoted(
        byte[] destination,
        int position,
        ReadOnlySpan<byte> encoded)
    {
        destination[position++] = (byte)'"';
        encoded.CopyTo(destination.AsSpan(position));
        position += encoded.Length;
        destination[position++] = (byte)'"';
        return position;
    }

    /// <summary>
    /// Writes <c>$type</c> and then each of the event's properties through <paramref name="writer"/>, so its
    /// indentation and escaping apply to them.
    /// </summary>
    private static void WriteMemberByMember(
        Utf8JsonWriter writer,
        IDomainEvent value,
        Type eventType,
        string typeName,
        JsonSerializerOptions options)
    {
        using var members = JsonSerializer.SerializeToDocument(
            value,
            eventType,
            options);

        writer.WriteStartObject();
        writer.WriteString(
            TypeProperty,
            typeName);

        foreach (var property in members.RootElement.EnumerateObject())
        {
            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>A type's assembly-qualified name, and the name escaped by the default encoder.</summary>
    private sealed class TypeName(string name)
    {
        public string Name { get; } = name;

        public JsonEncodedText Encoded { get; } = JsonEncodedText.Encode(name);
    }
}
