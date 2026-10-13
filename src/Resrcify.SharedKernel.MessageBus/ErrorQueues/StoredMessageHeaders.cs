using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Rebus.Messages;
using Resrcify.SharedKernel.Abstractions.MessageBus;
using Resrcify.SharedKernel.MessageBus.Serialization;

namespace Resrcify.SharedKernel.MessageBus.ErrorQueues;

/// <summary>Reads a stored message's headers, and turns it into a <see cref="FailedMessage"/>.</summary>
internal static class StoredMessageHeaders
{
    /// <summary>
    /// The headers a message loses when it is moved back: why it failed and where (Rebus' dead-letter headers), and the
    /// broker's count of its deliveries from the error queue.
    /// </summary>
    private static readonly HashSet<string> ErrorHeaders = new(StringComparer.Ordinal)
    {
        Headers.ErrorDetails,
        Headers.SourceQueue,
        "x-delivery-count",
    };

    public static string? IdOf(StoredMessage message)
        => message.Headers.GetValueOrDefault(Headers.MessageId);

    public static string? TypeOf(StoredMessage message)
        => message.Headers.GetValueOrDefault(Headers.Type);

    public static string? SourceQueueOf(StoredMessage message)
        => message.Headers.GetValueOrDefault(Headers.SourceQueue);

    /// <summary>Whether a header is one a message loses when it is moved back.</summary>
    public static bool IsErrorHeader(string name)
        => ErrorHeaders.Contains(name);

    /// <summary>The headers a message moved back keeps.</summary>
    public static Dictionary<string, string> WithoutErrorHeaders(IReadOnlyDictionary<string, string> headers)
        => headers
            .Where(header => !IsErrorHeader(header.Key))
            .ToDictionary(header => header.Key, header => header.Value, StringComparer.Ordinal);

    public static FailedMessage ToFailedMessage(StoredMessage message)
    {
        var body = Unzipped(message);
        return new FailedMessage(
            IdOf(message) ?? string.Empty,
            TypeOf(message),
            SourceQueueOf(message),
            message.Headers.GetValueOrDefault(Headers.ErrorDetails),
            SentOn(message),
            message.Headers,
            Text(message, body),
            body);
    }

    // The body unzipped, when it was gzipped and unzips; otherwise as stored.
    private static ReadOnlyMemory<byte> Unzipped(StoredMessage message)
    {
        if (!MessageCompression.IsGzipped(message.Headers))
            return message.Body;
        try
        {
            return MessageCompression.Unzip(message.Body.ToArray());
        }
        catch (InvalidDataException)
        {
            return message.Body;
        }
    }

    // JSON and text bodies, as text; others (e.g. a binary serialization) have none.
    private static string? Text(StoredMessage message, ReadOnlyMemory<byte> body)
    {
        var contentType = message.Headers.GetValueOrDefault(Headers.ContentType);
        if (contentType is null)
            return null;
        var isText = contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
        if (!isText)
            return null;
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(body.Span);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static DateTimeOffset? SentOn(StoredMessage message)
        => message.Headers.TryGetValue(Headers.SentTime, out var sent)
            && DateTimeOffset.TryParse(sent, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var sentOn)
                ? sentOn.ToUniversalTime()
                : null;
}
