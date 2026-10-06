using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Results.Serialization;

/// <summary>
/// Writes and reads <see cref="Result"/> and <see cref="Result{TValue}"/> as
/// <c>{ "isSuccess", "isFailure", "errors", "value" }</c> (names follow the serializer's naming policy). A failure is
/// written without a value, so serializing one doesn't touch <see cref="Result{TValue}.Value"/> (which throws on a
/// failure). Reading matches names case-insensitively and goes through <c>Result.Success</c> / <c>Result.Failure</c>,
/// so a payload that breaks a result's rules (a failure without errors) is rejected.
/// </summary>
internal sealed class ResultJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert == typeof(Result)
            || typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => typeToConvert == typeof(Result)
            ? new ResultConverter()
            : (JsonConverter?)Activator.CreateInstance(
                typeof(ValueResultConverter<>).MakeGenericType(typeToConvert.GenericTypeArguments[0]));

    private sealed class ResultConverter : JsonConverter<Result>
    {
        public override Result Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var (isSuccess, errors) = ResultJson.Read<object>(ref reader, options, out _);
            return ResultJson.Create(() => isSuccess ? Result.Success() : Result.Failure(errors));
        }

        public override void Write(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            ResultJson.WriteState(writer, value, options);
            writer.WriteEndObject();
        }
    }

    private sealed class ValueResultConverter<TValue> : JsonConverter<Result<TValue>>
    {
        public override Result<TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var (isSuccess, errors) = ResultJson.Read(ref reader, options, out TValue? value);
            return ResultJson.Create(() => isSuccess ? Result.Success(value!) : Result.Failure<TValue>(errors));
        }

        public override void Write(Utf8JsonWriter writer, Result<TValue> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (value.IsSuccess)
            {
                writer.WritePropertyName(ResultJson.Name("Value", options));
                JsonSerializer.Serialize(writer, value.Value, options);
            }
            ResultJson.WriteState(writer, value, options);
            writer.WriteEndObject();
        }
    }
}

internal static class ResultJson
{
    public static string Name(string name, JsonSerializerOptions options)
        => options.PropertyNamingPolicy?.ConvertName(name) ?? name;

    public static void WriteState(Utf8JsonWriter writer, Result result, JsonSerializerOptions options)
    {
        writer.WriteBoolean(Name("IsSuccess", options), result.IsSuccess);
        writer.WriteBoolean(Name("IsFailure", options), result.IsFailure);
        writer.WritePropertyName(Name("Errors", options));
        JsonSerializer.Serialize(writer, result.Errors, options);
    }

    public static (bool IsSuccess, IReadOnlyList<Error> Errors) Read<TValue>(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options,
        out TValue? value)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A result is a JSON object.");

        value = default;
        bool? isSuccess = null;
        IReadOnlyList<Error> errors = [];

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = reader.GetString();
            reader.Read();

            if (string.Equals(property, "IsSuccess", StringComparison.OrdinalIgnoreCase))
                isSuccess = reader.GetBoolean();
            else if (string.Equals(property, "Errors", StringComparison.OrdinalIgnoreCase))
                errors = JsonSerializer.Deserialize<Error[]>(ref reader, options) ?? [];
            else if (string.Equals(property, "Value", StringComparison.OrdinalIgnoreCase))
                value = JsonSerializer.Deserialize<TValue>(ref reader, options);
            else
                reader.Skip();
        }

        return isSuccess is { } success
            ? (success, errors)
            : throw new JsonException("A result needs \"isSuccess\".");
    }

    // A payload that breaks a result's rules is bad JSON to the caller, not an argument error.
    public static TResult Create<TResult>(Func<TResult> create)
    {
        try
        {
            return create();
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }
}
