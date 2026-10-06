using System;
using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.UnitOfWork.Converters;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Converters;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class SingleValueObjectConverterTests
{
    [Fact]
    public void ConvertToProvider_ShouldWriteTheValue()
    {
        var converter = new SingleValueObjectConverter<AllyCode, long>();

        converter.ConvertToProvider(AllyCode.Create(123_456_789).Value).ShouldBe(123_456_789L);
    }

    [Fact]
    public void ConvertFromProvider_ShouldCreateTheValueObject()
    {
        var converter = new SingleValueObjectConverter<PlayerId, string>();

        converter.ConvertFromProvider("P42").ShouldBe(PlayerId.Create("P42").Value);
    }

    [Fact]
    public void ConvertFromProvider_ShouldThrowAsCreateValueDoes_WhenTheValueDoesNotValidate()
    {
        var converter = new SingleValueObjectConverter<PlayerId, string>();

        var actual = Should.Throw<InvalidOperationException>(() => converter.ConvertFromProvider("not a player"));
        var expected = Should.Throw<InvalidOperationException>(() => PlayerId.Create("not a player").Value);

        actual.Message.ShouldBe(expected.Message);
    }

    [Fact]
    public void ConvertFromProvider_ShouldCallTheValueObjectsOwnFromPersisted_WhenItHasOne()
    {
        var converter = new SingleValueObjectConverter<TrustedCode, string>();

        var code = converter.ConvertFromProvider("TOOLONG").ShouldBeOfType<TrustedCode>();

        code.Value.ShouldBe("TOOLONG");
        TrustedCode.Create("TOOLONG").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Equals_ShouldBeTrue_ForAnyTwoConvertersOfTheSameValueObject()
    {
        var first = new SingleValueObjectConverter<PlayerId, string>();
        var second = new SingleValueObjectConverter<PlayerId, string>();

        first.Equals(second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
        first.Equals(new SingleValueObjectConverter<TrustedCode, string>()).ShouldBeFalse();
    }
}
