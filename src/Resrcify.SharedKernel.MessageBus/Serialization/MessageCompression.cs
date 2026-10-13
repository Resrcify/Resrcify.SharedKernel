using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Rebus.Messages;

namespace Resrcify.SharedKernel.MessageBus.Serialization;

/// <summary>
/// Gzip of message bodies, on the wire as Rebus' own compression (<c>EnableCompression</c>) writes it: the body
/// gzipped, marked with the <see cref="Headers.ContentEncoding"/> header set to <see cref="Gzip"/>. Either side reads
/// what the other writes. It differs in speed only: it compresses at <see cref="CompressionLevel.Fastest"/>
/// (Rebus: <see cref="CompressionLevel.Optimal"/>) and unzips into a buffer sized from the gzip trailer instead of a
/// growing stream.
/// </summary>
internal static class MessageCompression
{
    /// <summary>The <see cref="Headers.ContentEncoding"/> value of a gzipped body.</summary>
    public const string Gzip = "gzip";

    // A gzip stream is at least its header (10 bytes) and trailer (CRC-32 and ISIZE, 8 bytes).
    private const int MinimumGzipLength = 18;

    // Deflate can't compress better than about 1032:1, so a trailer claiming more is not to be trusted.
    private const long MaximumDeflateRatio = 1032;

    /// <summary>Whether <paramref name="headers"/> mark a gzipped body.</summary>
    public static bool IsGzipped(
        IReadOnlyDictionary<string, string> headers)
        => headers.TryGetValue(Headers.ContentEncoding, out var encoding)
            && string.Equals(encoding, Gzip, StringComparison.Ordinal);

    /// <summary>Gzips <paramref name="body"/> at <see cref="CompressionLevel.Fastest"/>.</summary>
    public static byte[] Zip(
        byte[] body)
    {
        using var compressed = new MemoryStream(EstimatedZippedLength(body.Length));
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(body);

        return compressed.ToArray();
    }

    /// <summary>
    /// Unzips <paramref name="compressed"/>. The uncompressed length is read from the gzip trailer (ISIZE) and the
    /// body read into a buffer of that size; a trailer that is implausible, or turns out wrong, falls back to growing
    /// the buffer as it reads.
    /// </summary>
    public static byte[] Unzip(
        byte[] compressed)
    {
        using var source = new MemoryStream(compressed, writable: false);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);

        return TrustedUncompressedLength(compressed) is { } length
            ? ReadPresized(gzip, length)
            : ReadGrowing(gzip, compressed.Length * 4L);
    }

    // The ISIZE trailer (the uncompressed length modulo 2^32, little-endian), when it can be believed: the stream is
    // long enough to have one, and it claims no more than deflate could have compressed into this many bytes.
    private static int? TrustedUncompressedLength(
        byte[] compressed)
    {
        if (compressed.Length < MinimumGzipLength)
            return null;

        var trailer = compressed.AsSpan(compressed.Length - sizeof(uint));
        long claimed = BinaryPrimitives.ReadUInt32LittleEndian(trailer);
        var plausible = Math.Min(compressed.Length * MaximumDeflateRatio, Array.MaxLength);

        return claimed <= plausible
            ? (int)claimed
            : null;
    }

    private static byte[] ReadPresized(
        GZipStream gzip,
        int length)
    {
        var buffer = GC.AllocateUninitializedArray<byte>(length);
        var read = gzip.ReadAtLeast(buffer, length, throwOnEndOfStream: false);

        // Shorter than the trailer said.
        if (read < length)
            return buffer.AsSpan(0, read).ToArray();

        // Exactly as long, unless more follows (a body over 4 GB, or several gzip members).
        var next = gzip.ReadByte();
        if (next < 0)
            return buffer;

        using var uncompressed = new MemoryStream(Capacity(length * 2L));
        uncompressed.Write(buffer);
        uncompressed.WriteByte((byte)next);
        gzip.CopyTo(uncompressed);
        return uncompressed.ToArray();
    }

    private static byte[] ReadGrowing(
        GZipStream gzip,
        long initialCapacity)
    {
        using var uncompressed = new MemoryStream(Capacity(initialCapacity));
        gzip.CopyTo(uncompressed);
        return uncompressed.ToArray();
    }

    private static int Capacity(
        long wanted)
        => (int)Math.Min(wanted, Array.MaxLength);

    // At the fastest level JSON shrinks to between a fifth and a half of its size: one buffer, without growing it,
    // and the stream still grows for a body that doesn't compress.
    private static int EstimatedZippedLength(
        int bodyLength)
        => bodyLength / 2 + MinimumGzipLength;
}
