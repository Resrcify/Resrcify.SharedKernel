using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Results.Serialization;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Results.UnitTests.Serialization;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ErrorTypeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new ErrorTypeJsonConverter() } };

    [Fact]
    public void Write_ShouldWriteTheName()
        => JsonSerializer.Serialize(ErrorType.Unprocessable, Options).ShouldBe("\"Unprocessable\"");

    [Theory]
    [InlineData("\"Unprocessable\"", ErrorType.Unprocessable)]
    [InlineData("\"notFound\"", ErrorType.NotFound)]
    [InlineData("2", ErrorType.NotFound)]
    public void Read_ShouldReadANameInAnyCase_OrANumber(string json, ErrorType expected)
        => JsonSerializer.Deserialize<ErrorType>(json, Options).ShouldBe(expected);

    [Theory]
    [InlineData("\"SomethingNewer\"")]
    [InlineData("99")]
    public void Read_ShouldReadAFailure_ForATypeThisVersionDoesNotKnow(string json)
        => JsonSerializer.Deserialize<ErrorType>(json, Options).ShouldBe(ErrorType.Failure);

    [Fact]
    public void Read_ShouldThrow_ForAnythingElse()
        => Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ErrorType>("true", Options));
}
