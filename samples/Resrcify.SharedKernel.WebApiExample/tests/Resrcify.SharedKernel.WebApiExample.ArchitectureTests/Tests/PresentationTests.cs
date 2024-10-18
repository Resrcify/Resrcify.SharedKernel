using NetArchTest.Rules;
using Resrcify.SharedKernel.Web.Primitives;
using Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Extensions;
using Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Helpers;

namespace Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Tests;

public class PresentationTests : BaseTest
{
    [Fact]
    public void Controllers_Should_HaveDependecyOnMediatR()
        => Types
            .InAssembly(PresentationAssembly)
            .That()
            .HaveNameEndingWith("Controller")
            .Should()
            .HaveDependencyOn("MediatR")
            .Evaluate();


    [Fact]
    public void Controllers_Should_ImplementApiController()
        => Types
            .InAssembly(PresentationAssembly)
            .That()
            .HaveNameEndingWith("Controller")
            .And()
            .AreNotAbstract()
            .Should().Inherit(typeof(ApiController))
            .Evaluate();
}
