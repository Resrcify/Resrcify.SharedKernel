using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.Primitives;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Extensions;

public class InternalControllersExtensionTests
{

    [Fact]
    public void EnableInternalControllers_ShouldAddCustomFeatureProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddMvc();

        // Act
        builder.EnableInternalControllers();

        // Assert
        var partManager = builder.Services
            .FirstOrDefault(s => s.ServiceType == typeof(ApplicationPartManager))?
            .ImplementationInstance as ApplicationPartManager;

        partManager.Should().NotBeNull();

        var providerType = Type.GetType("Resrcify.SharedKernel.Web.Extensions.InternalControllersExtensions+CustomControllerFeatureProvider, Resrcify.SharedKernel.Web");
        providerType.Should().NotBeNull();

        var providerInstance = partManager!.FeatureProviders.FirstOrDefault(fp => fp.GetType() == providerType);
        providerInstance.Should().NotBeNull();
    }
    private readonly ControllerFeatureProvider _provider;

    public InternalControllersExtensionTests()
    {
        var providerType = Type.GetType("Resrcify.SharedKernel.Web.Extensions.InternalControllersExtensions+CustomControllerFeatureProvider, Resrcify.SharedKernel.Web");
        providerType.Should().NotBeNull("CustomControllerFeatureProvider should exist");

        _provider = (ControllerFeatureProvider)Activator.CreateInstance(providerType!)!;
    }

    [Fact]
    public void IsController_ShouldReturnTrue_ForApiControllerSubclass()
    {
        // Arrange
        var typeInfo = typeof(TestController).GetTypeInfo();

        // Act
        var result = (bool)_provider.GetType()
            .GetMethod("IsController", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(_provider, [typeInfo])!;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsController_ShouldReturnFalse_ForAbstractController()
    {
        // Arrange
        var typeInfo = typeof(AbstractController).GetTypeInfo();

        // Act
        var result = (bool)_provider.GetType()
            .GetMethod("IsController", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(_provider, [typeInfo])!;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsController_ShouldReturnFalse_ForStandardMvcController()
    {
        // Arrange
        var typeInfo = typeof(MvcController).GetTypeInfo();

        // Act
        var result = (bool)_provider.GetType()
            .GetMethod("IsController", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(_provider, [typeInfo])!;

        // Assert
        result.Should().BeFalse();
    }

    private class TestController : ApiController
    {
        public TestController() : base(null!) { }
    }

    private abstract class AbstractController : ApiController
    {
        protected AbstractController() : base(null!) { }
    }

    private class MvcController : Controller { }
}
