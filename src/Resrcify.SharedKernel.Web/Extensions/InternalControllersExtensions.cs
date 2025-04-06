using System;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Web.Primitives;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class InternalControllersExtensions
{
    public static IMvcBuilder EnableInternalControllers(
        this IMvcBuilder builder,
        Type? controllerType = null)
         => builder.ConfigureApplicationPartManager(
                manager => manager.FeatureProviders.Add(
                    new CustomControllerFeatureProvider(controllerType)));

}
internal class CustomControllerFeatureProvider(Type? controllerType) : ControllerFeatureProvider
{
    public Type? ControllerType { get; } = controllerType;
    protected override bool IsController(TypeInfo typeInfo)
    {
        var selectedControllerType = ControllerType ?? typeof(ApiController);
        var isCustomController = !typeInfo.IsAbstract && selectedControllerType.IsAssignableFrom(typeInfo);
        return isCustomController || base.IsController(typeInfo);
    }
}