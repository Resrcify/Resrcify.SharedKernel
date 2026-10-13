using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Rebus.Compression;
using Rebus.Messages;
using Resrcify.SharedKernel.MessageBus.Serialization;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Serialization;

/// <summary>The gzip of message bodies, against Rebus' own <see cref="Zipper"/> in both directions.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class MessageCompressionTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Range(0, 5_000).Select(i => $"{{\"id\":{i},\"name\":\"unit-{i % 37}\",\"power\":{i * 7 % 1000}}},")));

    [Fact]
    public void Unzip_ShouldReadTheBody_WhenRebusZippedIt()
    {
        var zippedByRebus = new Zipper().Zip(Body);

        MessageCompression.Unzip(zippedByRebus).ShouldBe(Body);
    }

    [Fact]
    public void Zip_ShouldWriteWhatRebusUnzips()
    {
        var zipped = MessageCompression.Zip(Body);

        new Zipper().Unzip(zipped).ShouldBe(Body);
    }

    [Fact]
    public void Zip_ShouldShrinkTheBody_WhenItRepeatsItself()
    {
        var zipped = MessageCompression.Zip(Body);

        zipped.Length.ShouldBeLessThan(Body.Length / 2);
    }

    [Fact]
    public void Unzip_ShouldReturnAnEmptyBody_WhenAnEmptyBodyWasZipped()
    {
        var zipped = MessageCompression.Zip([]);

        MessageCompression.Unzip(zipped).ShouldBeEmpty();
    }

    [Fact]
    public void Unzip_ShouldReadTheWholeBody_WhenItCompressesAsWellAsDeflateAllows()
    {
        var zeros = new byte[4 * 1024 * 1024];

        MessageCompression.Unzip(MessageCompression.Zip(zeros)).ShouldBe(zeros);
    }

    [Fact]
    public void Unzip_ShouldReadEveryMember_WhenTheTrailerOnlyCountsTheLastOne()
    {
        // Two gzip members back to back: the trailer (ISIZE) is the second member's length only, so the buffer
        // sized from it is too small and the rest has to be read on top.
        var first = Body;
        var second = Encoding.UTF8.GetBytes("the second member");
        var concatenated = MessageCompression.Zip(first).Concat(MessageCompression.Zip(second)).ToArray();

        MessageCompression.Unzip(concatenated).ShouldBe(first.Concat(second).ToArray());
    }

    [Theory]
    [InlineData("gzip", true)]
    [InlineData("deflate", false)]
    [InlineData("GZIP", false)]
    public void IsGzipped_ShouldMatchTheContentEncodingRebusWrites(
        string encoding,
        bool expected)
    {
        var headers = new Dictionary<string, string> { [Headers.ContentEncoding] = encoding };

        MessageCompression.IsGzipped(headers).ShouldBe(expected);
    }

    [Fact]
    public void IsGzipped_ShouldBeFalse_WhenThereIsNoContentEncoding()
        => MessageCompression.IsGzipped(new Dictionary<string, string>()).ShouldBeFalse();
}
