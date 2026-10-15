using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Results.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ErrorCodesTests
{
    [Fact]
    public void EveryCode_ShouldBeDistinct()
    {
        var codes = Constants().Select(constant => constant.Value).ToList();

        codes.ShouldNotBeEmpty();
        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Count);
    }

    [Fact]
    public void EveryCode_ShouldStartWithItsGroup_ButTheRequestOnes()
    {
        // Request.Unhandled ("Unhandled") and Request.Binding ("Request") keep the codes services already match.
        foreach (var (group, name, value) in Constants().Where(constant => constant.Group != nameof(ErrorCodes.Request)))
        {
            var prefix = group == nameof(ErrorCodes.General) ? "Error" : group;
            value.ShouldBe($"{prefix}.{name}");
        }
    }

    [Fact]
    public void Request_ShouldKeepTheCodesTheWebPackageAnsweredWith()
    {
        ErrorCodes.Request.Unhandled.ShouldBe("Unhandled");
        ErrorCodes.Request.Binding.ShouldBe("Request");
    }

    [Theory]
    [InlineData(503, "Http.503")]
    [InlineData(404, "Http.404")]
    public void ForStatus_ShouldNameTheStatus(int status, string code)
        => ErrorCodes.Http.ForStatus(status).ShouldBe(code);

    [Fact]
    public void ResponderFailed_ShouldNameTheRequestType()
        => ErrorCodes.ScatterGather.ResponderFailed("PlayerQuestion").ShouldBe("PlayerQuestion.ResponderFailed");

    [Fact]
    public void ErrorsOfResults_ShouldUseTheCatalogue()
    {
        Error.NullValue.Code.ShouldBe(ErrorCodes.General.NullValue);
        Error.EmptyInput.Code.ShouldBe(ErrorCodes.General.EmptyInput);
    }

    [Fact]
    public void AConcurrencyConflict_ShouldBeTransient_ByItsCatalogueCode()
        => Error.Conflict(ErrorCodes.Persistence.Concurrency, "Changed since it was read.").IsTransient().ShouldBeTrue();

    private static (string Group, string Name, string Value)[] Constants()
        => [.. typeof(ErrorCodes)
            .GetNestedTypes()
            .SelectMany(group => group
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral)
                .Select(field => (group.Name, field.Name, (string)field.GetRawConstantValue()!)))];
}
