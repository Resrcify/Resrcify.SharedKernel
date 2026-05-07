using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NetArchTest.Rules;
using Resrcify.SharedKernel.Abstractions.Web;
using Resrcify.SharedKernel.ArchitectureTesting.Extensions;
using Resrcify.SharedKernel.ArchitectureTesting.Helpers;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.ArchitectureTesting.Tests;

/// <summary>
/// Conventions for the Presentation layer (minimal-API endpoints + optional
/// legacy controllers):
/// - Types ending with "Endpoint" implement <see cref="IEndpoint"/>, are
///   sealed, expose exactly one method named <c>MapEndpoint</c>, and depend
///   on <see cref="Resrcify.SharedKernel.Abstractions.Messaging"/>.
/// - Types ending with "Controller" depend on
///   <see cref="Resrcify.SharedKernel.Abstractions.Messaging"/> and inherit
///   from <c>Resrcify.SharedKernel.Web.Primitives.ApiController</c> (matched
///   by full type name to avoid pulling <c>SharedKernel.Web</c> into this
///   package's transitive closure).
///
/// Rules where the filtered type set is empty are pass-through, so a service
/// with no controllers (or no endpoints) is fine.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public abstract bases via inheritance.")]
public abstract class ConventionalPresentationTests : BaseArchitectureTest
{
    private const string Layer = "Presentation";
    private const string MessagingNamespace = "Resrcify.SharedKernel.Abstractions.Messaging";

    [SkippableFact]
    public virtual void Endpoints_Should_ImplementIEndpoint()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .HaveNameEndingWith("Endpoint", StringComparison.Ordinal)
            .And()
            .AreNotAbstract()
            .Should()
            .ImplementInterface(typeof(IEndpoint))
            .Evaluate();
    }

    [SkippableFact]
    public virtual void Endpoints_Should_BeSealed()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .HaveNameEndingWith("Endpoint", StringComparison.Ordinal)
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed()
            .Evaluate();
    }

    [SkippableFact]
    public virtual void Endpoints_Should_DependOnMessaging()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .HaveNameEndingWith("Endpoint", StringComparison.Ordinal)
            .Should()
            .HaveDependencyOn(MessagingNamespace)
            .Evaluate();
    }

    [SkippableFact]
    public virtual void Endpoints_Should_ExposeSingleMapEndpointMethod()
    {
        SkipIfNoAssembly(Layer);

        var endpoints = Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .HaveNameEndingWith("Endpoint", StringComparison.Ordinal)
            .And()
            .AreNotAbstract()
            .GetTypes()
            .ToList();

        foreach (var endpoint in endpoints)
        {
            var mapMethods = endpoint
                .GetMethods()
                .Where(m => m.Name == nameof(IEndpoint.MapEndpoint))
                .ToList();
            Assert.Single(mapMethods);
        }
    }

    [SkippableFact]
    public virtual void Controllers_Should_DependOnMessaging()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .HaveNameEndingWith("Controller", StringComparison.Ordinal)
            .Should()
            .HaveDependencyOn(MessagingNamespace)
            .Evaluate();
    }

    [SkippableFact]
    public virtual void Controllers_Should_InheritApiController()
    {
        SkipIfNoAssembly(Layer);

        const string ApiControllerFullName = "Resrcify.SharedKernel.Web.Primitives.ApiController";

        var controllers = Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .HaveNameEndingWith("Controller", StringComparison.Ordinal)
            .And()
            .AreNotAbstract()
            .GetTypes();

        var failing = new List<Type>();
        foreach (var controller in controllers)
        {
            var inheritsApiController = false;
            for (var t = controller.BaseType; t is not null; t = t.BaseType)
            {
                if (string.Equals(t.FullName, ApiControllerFullName, StringComparison.Ordinal))
                {
                    inheritsApiController = true;
                    break;
                }
            }

            if (!inheritsApiController)
                failing.Add(controller);
        }

        failing.ShouldBeEmpty();
    }
}
