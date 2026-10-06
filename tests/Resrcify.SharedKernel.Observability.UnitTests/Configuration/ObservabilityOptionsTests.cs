using System;
using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.Observability.Configuration;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Observability.UnitTests.Configuration;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ObservabilityOptionsTests
{
    [Theory]
    [InlineData("http://tempo.monitoring:4317")]
    [InlineData("https://otlp.example.com")]
    public void OtlpEndpointUri_ShouldBeTheEndpoint_WhenItIsAnHttpUri(
        string endpoint)
    {
        var options = new ObservabilityOptions { OtlpEndpoint = endpoint };

        options.OtlpEndpointUri.ShouldBe(new Uri(endpoint));
        options.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void IsValid_ShouldBeTrue_WhenTheEndpointIsEmpty(
        string? endpoint)
    {
        var options = new ObservabilityOptions { OtlpEndpoint = endpoint };

        options.IsValid.ShouldBeTrue();
        options.OtlpEndpointUri.ShouldBeNull();
    }

    [Theory]
    [InlineData("tempo.monitoring:4317")]
    [InlineData("/v1/traces")]
    [InlineData("ftp://tempo.monitoring")]
    public void IsValid_ShouldBeFalse_WhenTheEndpointIsNotAnAbsoluteHttpUri(
        string endpoint)
    {
        var options = new ObservabilityOptions { OtlpEndpoint = endpoint };

        options.IsValid.ShouldBeFalse();
        options.OtlpEndpointUri.ShouldBeNull();
    }
}
