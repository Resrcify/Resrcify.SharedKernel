using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Resrcify.SharedKernel.Abstractions.Web;
using Resrcify.SharedKernel.Web.Extensions;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

internal sealed class FakeEndpoint : IEndpoint
{
    public bool WasMapped { get; private set; }

    public void MapEndpoint(
        IEndpointRouteBuilder app)
    {
        WasMapped = true;
    }
}

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class EndpointExtensionsTests
{
    [Fact]
    public void AddEndpoints_ShouldRegisterAllIEndpoints_FromGivenAssemblies()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddEndpoints(typeof(FakeEndpoint).Assembly);
        var provider = services.BuildServiceProvider();

        // Assert
        var endpoints = provider
            .GetServices<IEndpoint>()
            .ToArray();

        Assert.NotEmpty(endpoints);
        Assert.Contains(endpoints, e => e is FakeEndpoint);

        var descriptor = services.First(
            d => d.ImplementationType == typeof(FakeEndpoint));
        Assert.Equal(
            ServiceLifetime.Transient,
            descriptor.Lifetime);
    }

    [Fact]
    public void MapEndpoints_ShouldInvokeMapEndpoint_OnAllRegisteredEndpoints()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        var fakeEndpoint = Substitute.For<IEndpoint>();
        builder.Services.AddSingleton<IEnumerable<IEndpoint>>([fakeEndpoint]);

        var app = builder.Build();

        // Act
        app.MapEndpoints();

        // Assert
        fakeEndpoint
            .Received(1)
            .MapEndpoint(app);
    }
}