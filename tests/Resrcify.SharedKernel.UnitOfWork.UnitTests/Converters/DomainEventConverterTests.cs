using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.UnitOfWork.Converters;
using Resrcify.SharedKernel.UnitOfWork.Outbox;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Converters;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class DomainEventConverterTests
{
    private const string SampleTypeName =
        "Resrcify.SharedKernel.UnitOfWork.UnitTests.Converters.DomainEventConverterTests\\u002BStoredSampleEvent, "
        + "Resrcify.SharedKernel.UnitOfWork.UnitTests, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

    /// <summary>
    /// An outbox row written by the converter before it was rewritten to serialize without a string round trip
    /// (SharedKernel 4.0 development): rows like it are in outboxes now and must keep reading.
    /// </summary>
    private const string StoredRow =
        "{\"$type\":\"" + SampleTypeName + "\","
        + "\"Message\":\"\\u003Ca \\u0026 b\\u003E \\u0022quoted\\u0022 caf\\u00E9 \\u002B1\",\"Count\":42,\"Amount\":12.50,"
        + "\"OccurredAt\":\"2026-10-06T08:30:00Z\",\"Tags\":[\"first\",\"second\"],\"Detail\":{\"Name\":\"detail\",\"Flag\":true},"
        + "\"Inner\":{\"$type\":\"" + SampleTypeName + "\","
        + "\"Message\":\"inner\",\"Count\":1,\"Amount\":0,\"OccurredAt\":\"2026-01-01T00:00:00Z\",\"Tags\":[],"
        + "\"Detail\":{\"Name\":\"\",\"Flag\":false},\"Inner\":null,\"Id\":\"00000000-0000-0000-0000-000000000001\"},"
        + "\"Id\":\"6f1c2b8e-3d4a-4c5b-9e7f-0a1b2c3d4e5f\"}";

    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Converters = { new DomainEventConverter() }
    };

    private readonly SystemTextJsonOutboxSerializer _serializer = new();

    [Fact]
    public void Deserialize_ShouldReadTheEvent_WhenTheRowWasStoredByThePreviousConverter()
    {
        // Act
        var domainEvent = _serializer.Deserialize(StoredRow);

        // Assert
        var sample = domainEvent.ShouldBeOfType<StoredSampleEvent>();
        sample.Id.ShouldBe(new Guid("6f1c2b8e-3d4a-4c5b-9e7f-0a1b2c3d4e5f"));
        sample.Message.ShouldBe("<a & b> \"quoted\" café +1");
        sample.Count.ShouldBe(42);
        sample.Amount.ShouldBe(12.50m);
        sample.OccurredAt.ShouldBe(new DateTime(2026, 10, 6, 8, 30, 0, DateTimeKind.Utc));
        sample.Tags.ShouldBe(["first", "second"]);
        sample.Detail.ShouldBe(new SampleDetail("detail", Flag: true));
        var inner = sample.Inner.ShouldBeOfType<StoredSampleEvent>();
        inner.Id.ShouldBe(new Guid("00000000-0000-0000-0000-000000000001"));
        inner.Inner.ShouldBeNull();
    }

    [Fact]
    public void Serialize_ShouldWriteTheSameJson_AsThePreviousConverterStored()
        => _serializer.Serialize(Sample()).ShouldBe(StoredRow);

    [Fact]
    public void Deserialize_ShouldReadTheEvent_WhenTypeIsNotTheFirstProperty()
    {
        // Arrange
        var json = "{\"Id\":\"00000000-0000-0000-0000-000000000002\",\"$type\":\""
            + typeof(OtherSampleEvent).AssemblyQualifiedName + "\"}";

        // Act
        var domainEvent = _serializer.Deserialize(json);

        // Assert
        domainEvent.ShouldBe(new OtherSampleEvent(new Guid("00000000-0000-0000-0000-000000000002")));
    }

    [Fact]
    public void Deserialize_ShouldThrowEveryTime_WhenTheTypeCannotBeResolved()
    {
        // Arrange
        const string json = "{\"$type\":\"No.Such.Type, No.Such.Assembly\",\"Id\":\"00000000-0000-0000-0000-000000000003\"}";

        // Act & Assert: the second read finds the remembered miss.
        Should.Throw<InvalidOperationException>(() => _serializer.Deserialize(json))
            .Message.ShouldBe("Type 'No.Such.Type, No.Such.Assembly' could not be resolved.");
        Should.Throw<InvalidOperationException>(() => _serializer.Deserialize(json));
    }

    [Theory]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000004\"}")]
    [InlineData("{\"$type\":\"\",\"Id\":\"00000000-0000-0000-0000-000000000004\"}")]
    [InlineData("{\"$type\":null,\"Id\":\"00000000-0000-0000-0000-000000000004\"}")]
    public void Deserialize_ShouldThrowJsonException_WhenThereIsNoType(string json)
        => Should.Throw<JsonException>(() => _serializer.Deserialize(json));

    [Fact]
    public void Serialize_ShouldIndentEveryProperty_WhenTheOptionsAskForIt()
    {
        // Arrange
        var domainEvent = new OtherSampleEvent(new Guid("00000000-0000-0000-0000-000000000005"));

        // Act
        var json = JsonSerializer.Serialize<IDomainEvent>(domainEvent, IndentedOptions);

        // Assert
        json.ShouldBe(
            "{" + Environment.NewLine
            + "  \"$type\": \"" + typeof(OtherSampleEvent).AssemblyQualifiedName!.Replace("+", "\\u002B", StringComparison.Ordinal) + "\"," + Environment.NewLine
            + "  \"Id\": \"00000000-0000-0000-0000-000000000005\"" + Environment.NewLine
            + "}");
        JsonSerializer.Deserialize<IDomainEvent>(json, IndentedOptions).ShouldBe(domainEvent);
    }

    private static StoredSampleEvent Sample()
        => new(
            new Guid("6f1c2b8e-3d4a-4c5b-9e7f-0a1b2c3d4e5f"),
            "<a & b> \"quoted\" café +1",
            42,
            12.50m,
            new DateTime(2026, 10, 6, 8, 30, 0, DateTimeKind.Utc),
            ["first", "second"],
            new SampleDetail("detail", Flag: true),
            Inner: new StoredSampleEvent(
                new Guid("00000000-0000-0000-0000-000000000001"),
                "inner",
                1,
                0m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                [],
                new SampleDetail("", Flag: false),
                Inner: null));

    internal sealed record SampleDetail(string Name, bool Flag);

    internal sealed record StoredSampleEvent(
        Guid Id,
        string Message,
        int Count,
        decimal Amount,
        DateTime OccurredAt,
        IReadOnlyList<string> Tags,
        SampleDetail Detail,
        IDomainEvent? Inner)
        : DomainEvent(Id);

    internal sealed record OtherSampleEvent(Guid Id)
        : DomainEvent(Id);
}
