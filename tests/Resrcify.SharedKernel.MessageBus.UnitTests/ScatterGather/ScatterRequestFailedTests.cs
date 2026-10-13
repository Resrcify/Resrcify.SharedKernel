using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resrcify.SharedKernel.MessageBus.ScatterGather;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.ScatterGather;

/// <summary>The bus' own failure reply reads whatever enum settings the two services' serialization has.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ScatterRequestFailedTests
{
    private static readonly JsonSerializerOptions Plain = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions NamesOnly = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    [Fact]
    public void Deserialize_ShouldReadTheErrorType_WhenTheRequesterRefusesNumbers()
    {
        var json = JsonSerializer.Serialize(ScatterRequestFailed.From([Error.NotFound("Player.NotFound", "No player.")]), Plain);

        var failed = JsonSerializer.Deserialize<ScatterRequestFailed>(json, NamesOnly)!;

        failed.ToErrors().ShouldHaveSingleItem().Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void Deserialize_ShouldReadTheErrorTypeAsANumber_AsAnOlderResponderWroteIt()
    {
        const string json = """{"errors":[{"code":"Player.NotFound","message":"No player.","type":2}]}""";

        JsonSerializer.Deserialize<ScatterRequestFailed>(json, Plain)!.ToErrors().ShouldHaveSingleItem().Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void Serialize_ShouldWriteTheErrorTypeAsItsName()
        => JsonSerializer.Serialize(ScatterRequestFailed.From([Error.NotFound("Player.NotFound", "No player.")]), Plain)
            .ShouldContain("\"type\":\"NotFound\"");
}
