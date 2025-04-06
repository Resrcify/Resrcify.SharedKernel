using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.Primitives;
using System.Reflection;

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

        partManager!.FeatureProviders
            .OfType<CustomControllerFeatureProvider>()
            .Should()
            .ContainSingle(fp => fp.ControllerType == null);
    }

    [Fact]
    public void IsController_ShouldReturnTrue_ForApiControllerSubclass()
    {
        // Arrange
        var provider = new TestCustomControllerFeatureProvider(null);
        var typeInfo = typeof(TestController).GetTypeInfo();

        // Act
        var result = provider.IsController(typeInfo);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsController_ShouldReturnFalse_ForAbstractController()
    {
        // Arrange
        var provider = new TestCustomControllerFeatureProvider(null);
        var typeInfo = typeof(AbstractController).GetTypeInfo();

        // Act
        var result = provider.IsController(typeInfo);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsController_ShouldReturnFalse_ForStandardMvcController()
    {
        // Arrange
        var provider = new TestCustomControllerFeatureProvider(null);
        var typeInfo = typeof(MvcController).GetTypeInfo();

        // Act
        var result = provider.IsController(typeInfo);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsController_ShouldReturnTrue_ForCustomBaseController_WhenSpecified()
    {
        // Arrange
        var provider = new TestCustomControllerFeatureProvider(typeof(CustomBaseController));
        var typeInfo = typeof(CustomDerivedController).GetTypeInfo();

        // Act
        var result = provider.IsController(typeInfo);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsController_ShouldReturnFalse_ForApiController_WhenCustomTypeSpecified()
    {
        // Arrange
        var provider = new TestCustomControllerFeatureProvider(typeof(CustomBaseController));
        var typeInfo = typeof(TestController).GetTypeInfo();

        // Act
        var result = provider.IsController(typeInfo);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void EnableInternalControllers_WithCustomType_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddMvc();
        var customType = typeof(CustomBaseController);

        // Act
        builder.EnableInternalControllers(customType);

        // Assert
        var partManager = builder.PartManager;
        partManager.Should().NotBeNull();

        partManager!.FeatureProviders
            .OfType<CustomControllerFeatureProvider>()
            .Should()
            .ContainSingle(fp => fp.ControllerType == customType);
    }

    // ---------- Test Controller Types ----------

    private class TestController : ApiController
    {
        public TestController() : base(null!) { }
    }

    private abstract class AbstractController : ApiController
    {
        protected AbstractController() : base(null!) { }
    }

    private class MvcController : Controller { }

    private abstract class CustomBaseController { }

    private class CustomDerivedController : CustomBaseController { }

    // ---------- Subclass to expose IsController ----------
    internal class TestCustomControllerFeatureProvider : CustomControllerFeatureProvider
    {
        public TestCustomControllerFeatureProvider(Type? controllerType)
            : base(controllerType)
        {
        }

        public new bool IsController(TypeInfo typeInfo)
        {
            return base.IsController(typeInfo);
        }
    }
}
