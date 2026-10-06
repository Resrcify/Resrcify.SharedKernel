using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Results.UnitTests.Serialization;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ResultJsonConverterFactoryTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Serialize_ShouldWriteTheErrorsWithoutAValue_WhenTheResultIsAFailure()
    {
        var json = JsonSerializer.Serialize(Result.Failure<int>(Error.NotFound("Player.NotFound", "No player.")), Web);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("isSuccess").GetBoolean().ShouldBeFalse();
        document.RootElement.GetProperty("isFailure").GetBoolean().ShouldBeTrue();
        document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("Player.NotFound");
        document.RootElement.TryGetProperty("value", out _).ShouldBeFalse();
    }

    [Fact]
    public void Deserialize_ShouldRoundTripASuccess_WhenTheResultHasAValue()
    {
        var json = JsonSerializer.Serialize(Result.Success(new Player("Han")), Web);

        var result = JsonSerializer.Deserialize<Result<Player>>(json, Web)!;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new Player("Han"));
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Deserialize_ShouldRoundTripAFailure_WhenTheResultHasErrors()
    {
        var error = Error.Conflict("Player.Taken", "Taken.");
        var json = JsonSerializer.Serialize(Result.Failure<Player>(error), Web);

        var result = JsonSerializer.Deserialize<Result<Player>>(json, Web)!;

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([error]);
    }

    [Fact]
    public void Deserialize_ShouldRoundTripANonGenericResult_WhenItFailed()
    {
        var json = JsonSerializer.Serialize(Result.Failure(Error.NullValue));

        var result = JsonSerializer.Deserialize<Result>(json)!;

        result.Errors.ShouldBe([Error.NullValue]);
    }

    [Fact]
    public void Deserialize_ShouldReadTheShapeWrittenBefore40_WhenTheNamesArePascalCase()
    {
        // What a cache held before 4.0: System.Text.Json's default shape, with PascalCase names.
        const string json = """{"Value":{"Name":"Han"},"IsSuccess":true,"IsFailure":false,"Errors":[]}""";

        var result = JsonSerializer.Deserialize<Result<Player>>(json, Web)!;

        result.Value.ShouldBe(new Player("Han"));
    }

    [Fact]
    public void Deserialize_ShouldThrowAJsonException_WhenAFailureHasNoErrors()
        => Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Result<Player>>("""{"isSuccess":false,"errors":[]}""", Web));

    [Fact]
    public void Deserialize_ShouldThrowAJsonException_WhenIsSuccessIsMissing()
        => Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<Result>("""{"errors":[]}""", Web));

    [SuppressMessage(
        "Performance",
        "CA1515:Consider making public types internal",
        Justification = "Serialized by System.Text.Json in the tests.")]
    public sealed record Player(string Name);
}
