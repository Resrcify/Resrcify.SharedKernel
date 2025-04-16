using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.Web.Primitives;
using System.Reflection;
using Shouldly;

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

        partManager.ShouldNotBeNull();

        partManager.FeatureProviders
            .OfType<CustomControllerFeatureProvider>()
            .ShouldContain(fp => fp.ControllerType == null, "Expected a single provider with a null ControllerType");
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
        result.ShouldBeTrue();
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
        result.ShouldBeFalse();
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
        result.ShouldBeFalse();
    }

    [Fact]
    public void IsController_ShouldReturnTrue_ForCustomBaseController_WhenSpecified()
    {
        // Arrange
        var provider = new TestCustomControllerFeatureProvider(typeof(ICustomBaseController));
        var typeInfo = typeof(CustomDerivedController).GetTypeInfo();

        // Act
        var result = provider.IsController(typeInfo);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void IsController_ShouldReturnFalse_ForApiController_WhenCustomTypeSpecified()
    {
        // Arrange
        var provider = new TestCustomControllerFeatureProvider(typeof(ICustomBaseController));
        var typeInfo = typeof(TestController).GetTypeInfo();

        // Act
        var result = provider.IsController(typeInfo);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void EnableInternalControllers_WithCustomType_ShouldRegisterProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddMvc();
        var customType = typeof(ICustomBaseController);

        // Act
        builder.EnableInternalControllers(customType);

        // Assert
        var partManager = builder.PartManager;
        partManager.ShouldNotBeNull();

        partManager.FeatureProviders
            .OfType<CustomControllerFeatureProvider>()
            .ShouldContain(fp => fp.ControllerType == customType, "Expected a single provider with the specified ControllerType");
    }

    // ---------- Test Controller Types ----------

    private sealed class TestController : ApiController
    {
        public TestController() : base(null!) { }
    }

    private abstract class AbstractController : ApiController
    {
        protected AbstractController() : base(null!) { }
    }

    private sealed class MvcController : Controller { }

    internal interface ICustomBaseController { }

    private sealed class CustomDerivedController : ICustomBaseController { }

    // ---------- Subclass to expose IsController ----------
    internal sealed class TestCustomControllerFeatureProvider : CustomControllerFeatureProvider
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
