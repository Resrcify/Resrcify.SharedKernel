using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Web.Primitives;

namespace Resrcify.SharedKernel.Web.Extensions;

public static class InternalControllersExtensions
{
    public static IMvcBuilder EnableInternalControllers(this IMvcBuilder builder)
         => builder.ConfigureApplicationPartManager(
                manager => manager.FeatureProviders.Add(
                    new CustomControllerFeatureProvider()));

    internal sealed class CustomControllerFeatureProvider : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo)
        {
            var isCustomController = !typeInfo.IsAbstract && typeof(ApiController).IsAssignableFrom(typeInfo);
            return isCustomController || base.IsController(typeInfo);
        }
    }

}