using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

/// <summary>EF Core's design-time services for a context, built the way <c>dotnet ef</c> builds them.</summary>
internal static class DesignTimeServices
{
    /// <summary>
    /// EF Core's design-time services, the context's, then the provider's (named by the provider assembly's
    /// <see cref="DesignTimeProviderServicesAttribute"/>).
    /// </summary>
    public static ServiceProvider For(DbContext context)
    {
        var collection = new ServiceCollection()
            .AddEntityFrameworkDesignTimeServices()
            .AddDbContextDesignTimeServices(context);
        var providerAssembly = Assembly.Load(new AssemblyName(context.Database.ProviderName!));
        var providerServicesType = providerAssembly.GetType(
            providerAssembly.GetCustomAttribute<DesignTimeProviderServicesAttribute>()!.TypeName,
            throwOnError: true)!;
        ((IDesignTimeServices)Activator.CreateInstance(providerServicesType)!).ConfigureDesignTimeServices(collection);
        return collection.BuildServiceProvider();
    }
}
