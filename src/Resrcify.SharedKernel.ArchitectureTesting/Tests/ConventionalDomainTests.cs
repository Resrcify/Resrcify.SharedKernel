using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using NetArchTest.Rules;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.ArchitectureTesting.Extensions;
using Resrcify.SharedKernel.ArchitectureTesting.Helpers;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.ArchitectureTesting.Tests;

/// <summary>
/// Conventions for the Domain layer:
/// - All <see cref="IDomainEvent"/> implementations are sealed and end with "Event".
/// - All <see cref="Entity{TId}"/>-derived (entities + aggregate roots) types
///   have a private constructor.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit discovers tests on public abstract bases via inheritance.")]
public abstract class ConventionalDomainTests : BaseArchitectureTest
{
    private const string Layer = "Domain";

    [SkippableFact]
    public virtual void DomainEvents_Should_BeSealed()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(IDomainEvent))
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed()
            .Evaluate();
    }

    [SkippableFact]
    public virtual void DomainEvents_Should_HaveEventPostfix()
    {
        SkipIfNoAssembly(Layer);

        Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .ImplementInterface(typeof(IDomainEvent))
            .Should()
            .HaveNameEndingWith("Event", StringComparison.Ordinal)
            .Evaluate();
    }

    [SkippableFact]
    public virtual void Entities_Should_HavePrivateConstructor()
    {
        SkipIfNoAssembly(Layer);

        var entities = Types
            .InAssembly(GetLayerAssembly(Layer)!)
            .That()
            .Inherit(typeof(Entity<>))
            .And()
            .AreNotAbstract()
            .GetTypes();

        var failingTypes = new List<Type>();
        foreach (var type in entities)
        {
            var ctors = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
            if (!ctors.Any(c => c.IsPrivate))
                failingTypes.Add(type);
        }

        failingTypes.ShouldBeEmpty();
    }
}
