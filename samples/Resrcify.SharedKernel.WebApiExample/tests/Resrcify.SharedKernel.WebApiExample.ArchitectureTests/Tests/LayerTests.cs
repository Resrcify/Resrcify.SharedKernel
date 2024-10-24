using NetArchTest.Rules;
using Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Extensions;
using Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Helpers;

namespace Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Tests;

public class LayerTests : BaseTest
{
    [Theory]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Application")]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Presentation")]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Infrastructure")]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Web")]
    public void Domain_Should_NotHaveDependecyOn(string assemblyName)
        => Types
            .InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn(assemblyName)
            .Evaluate();

    [Theory]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Presentation")]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Infrastructure")]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Persistence")]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Web")]
    public void Application_Should_NotHaveDependecyOn(string assemblyName)
        => Types
            .InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn(assemblyName)
            .Evaluate();

    [Theory]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Presentation")]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Web")]
    public void Infrastructure_Should_NotHaveDependecyOn(string assemblyName)
        => Types
            .InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn(assemblyName)
            .Evaluate();

    [Theory]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Presentation")]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Infrastructure")]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Web")]
    public void Persistence_Should_NotHaveDependecyOn(string assemblyName)
        => Types
            .InAssembly(PersistenceAssembly)
            .Should()
            .NotHaveDependencyOn(assemblyName)
            .Evaluate();

    [Theory]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Persistence")]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Infrastructure")]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Web")]
    public void Presentation_Should_NotHaveDependecyOn(string assemblyName)
        => Types
            .InAssembly(PresentationAssembly)
            .Should()
            .NotHaveDependencyOn(assemblyName)
            .Evaluate();

    [Theory]
    [InlineData("Resrcify.SharedKernel.WebApiExample.Domain")]
    public void Web_Should_NotHaveDependecyOn(string assemblyName)
        => Types
            .InAssembly(WebAssembly)
            .Should()
            .NotHaveDependencyOn(assemblyName)
            .Evaluate();
}
