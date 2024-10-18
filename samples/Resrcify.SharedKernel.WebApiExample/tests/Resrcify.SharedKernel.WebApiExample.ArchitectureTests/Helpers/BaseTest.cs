using System.Reflection;
using Resrcify.SharedKernel.WebApiExample.Application;
using Resrcify.SharedKernel.WebApiExample.Domain;
using Resrcify.SharedKernel.WebApiExample.Infrastructure;
using Resrcify.SharedKernel.WebApiExample.Persistence;
using Resrcify.SharedKernel.WebApiExample.Presentation;
using Resrcify.SharedKernel.WebApiExample.Web;

namespace Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Helpers;

public abstract class BaseTest
{
    protected static readonly Assembly DomainAssembly = typeof(AssemblyFlag).Assembly;
    protected static readonly Assembly ApplicationAssembly = typeof(ApplicationServiceRegistration).Assembly;
    protected static readonly Assembly InfrastructureAssembly = typeof(InfrastructureServiceRegistration).Assembly;
    protected static readonly Assembly PersistenceAssembly = typeof(PersistenceServiceRegistration).Assembly;
    protected static readonly Assembly PresentationAssembly = typeof(PresentationServiceRegistration).Assembly;
    protected static readonly Assembly WebAssembly = typeof(Program).Assembly;
}