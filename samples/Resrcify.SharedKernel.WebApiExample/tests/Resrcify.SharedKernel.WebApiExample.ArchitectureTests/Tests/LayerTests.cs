using NetArchTest.Rules;
using Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Extensions;
using Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Helpers;

namespace Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Tests;

public class LayerTests : BaseTest
{
    [Theory]
    [InlineData("Resrcify.Sandbox.Application")]
    [InlineData("Resrcify.Sandbox.Presentation")]
    [InlineData("Resrcify.Sandbox.Infrastructure")]
    [InlineData("Resrcify.Sandbox.Web")]
    public void Domain_Should_NotHaveDependecyOn(string assemblyName)
        => Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn(assemblyName)
            .Evaluate();

    [Theory]
    [InlineData("Resrcify.Sandbox.Presentation")]
    [InlineData("Resrcify.Sandbox.Infrastructure")]
    [InlineData("Resrcify.Sandbox.Persistence")]
    [InlineData("Resrcify.Sandbox.Web")]
    public void Application_Should_NotHaveDependecyOn(string assemblyName)
        => Types
            .InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn(assemblyName)
            .Evaluate();

    [Theory]
    [InlineData("Resrcify.Sandbox.Presentation")]
    [InlineData("Resrcify.Sandbox.Web")]
    public void Infrastructure_Should_NotHaveDependecyOn(string assemblyName)
        => Types
            .InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn(assemblyName)
            .Evaluate();

    [Theory]
    [InlineData("Resrcify.Sandbox.Presentation")]
    [InlineData("Resrcify.Sandbox.Infrastructure")]
    [InlineData("Resrcify.Sandbox.Web")]
    public void Persistence_Should_NotHaveDependecyOn(string assemblyName)
        => Types
            .InAssembly(PersistenceAssembly)
            .Should()
            .NotHaveDependencyOn(assemblyName)
            .Evaluate();

    [Theory]
    [InlineData("Resrcify.Sandbox.Persistence")]
    [InlineData("Resrcify.Sandbox.Infrastructure")]
    [InlineData("Resrcify.Sandbox.Web")]
    public void Presentation_Should_NotHaveDependecyOn(string assemblyName)
        => Types
            .InAssembly(PresentationAssembly)
            .Should()
            .NotHaveDependencyOn(assemblyName)
            .Evaluate();

    [Theory]
    [InlineData("Resrcify.Sandbox.Domain")]
    public void Web_Should_NotHaveDependecyOn(string assemblyName)
        => Types
            .InAssembly(WebAssembly)
            .Should()
            .NotHaveDependencyOn(assemblyName)
            .Evaluate();
}
