using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Resrcify.SharedKernel.Web.Health;

/// <summary>
/// Writes a <see cref="HealthReport"/> as JSON: the overall status and duration, and per check its status,
/// description, duration, data, error (the exception's message) and tags.
/// </summary>
/// <example>
/// <code>
/// {
///   "status": "Unhealthy",
///   "duration": "00:00:00.0123456",
///   "checks": {
///     "postgres": {
///       "status": "Unhealthy",
///       "description": "Connection refused",
///       "duration": "00:00:00.0100000",
///       "data": {},
///       "error": "Connection refused",
///       "tags": [ "ready" ]
///     }
///   }
/// }
/// </code>
/// </example>
public static class HealthResponseWriter
{
    private const string ContentType = "application/json; charset=utf-8";

    private static readonly JsonSerializerOptions DataOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false };

    /// <summary>A <c>HealthCheckOptions.ResponseWriter</c>.</summary>
    public static async Task WriteAsync(
        HttpContext context,
        HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = ContentType;

        await using var writer = new Utf8JsonWriter(context.Response.Body, WriterOptions);
        WriteReport(writer, report);
        await writer.FlushAsync(context.RequestAborted);
    }

    private static void WriteReport(
        Utf8JsonWriter writer,
        HealthReport report)
    {
        writer.WriteStartObject();
        writer.WriteString("status", report.Status.ToString());
        writer.WriteString("duration", Format(report.TotalDuration));

        writer.WriteStartObject("checks");
        foreach (var (name, entry) in report.Entries)
        {
            writer.WritePropertyName(name);
            WriteEntry(writer, entry);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteEntry(
        Utf8JsonWriter writer,
        HealthReportEntry entry)
    {
        writer.WriteStartObject();
        writer.WriteString("status", entry.Status.ToString());
        writer.WriteString("description", entry.Description);
        writer.WriteString("duration", Format(entry.Duration));
        WriteData(writer, entry.Data);
        writer.WriteString("error", entry.Exception?.Message);
        WriteTags(writer, entry.Tags);
        writer.WriteEndObject();
    }

    private static void WriteData(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, object> data)
    {
        writer.WriteStartObject("data");
        foreach (var (key, value) in data)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object), DataOptions);
        }

        writer.WriteEndObject();
    }

    private static void WriteTags(
        Utf8JsonWriter writer,
        IEnumerable<string> tags)
    {
        writer.WriteStartArray("tags");
        foreach (var tag in tags)
            writer.WriteStringValue(tag);
        writer.WriteEndArray();
    }

    private static string Format(
        TimeSpan duration)
        => duration.ToString("c", CultureInfo.InvariantCulture);
}
