using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Results.Serialization;

/// <summary>
/// Writes an <see cref="ErrorType"/> as its name, and reads a name (any case) or a number. A type this version doesn't
/// know, a newer service's, reads as <see cref="ErrorType.Failure"/> instead of failing the whole read: another
/// service's errors stay readable when it is upgraded first.
/// </summary>
public sealed class ErrorTypeJsonConverter : JsonConverter<ErrorType>
{
    public override ErrorType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => Enum.TryParse<ErrorType>(reader.GetString(), ignoreCase: true, out var named)
                && Enum.IsDefined(named)
                    ? named
                    : ErrorType.Failure,
            JsonTokenType.Number => reader.TryGetInt32(out var number) && Enum.IsDefined((ErrorType)number)
                ? (ErrorType)number
                : ErrorType.Failure,
            _ => throw new JsonException($"An error type is a name or a number, not {reader.TokenType}."),
        };

    public override void Write(Utf8JsonWriter writer, ErrorType value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}
