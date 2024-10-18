using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Resrcify.SharedKernel.DomainDrivenDesign.Abstractions;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Extensions;
using Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Helpers;
using Xunit.Abstractions;

namespace Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Tests;

public class DomainTests : BaseTest
{
    private readonly ITestOutputHelper _output;

    public DomainTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DomainEvents_Should_BeSealed()
        => Types
            .InAssembly(DomainAssembly)
            .That()
            .ImplementInterface(typeof(IDomainEvent))
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed()
            .Evaluate();

    [Fact]
    public void DomainEvents_Should_HaveEventPostFix()
        => Types
            .InAssembly(DomainAssembly)
            .That()
            .ImplementInterface(typeof(IDomainEvent))
            .Should()
            .HaveNameEndingWith("Event")
            .Evaluate();

    [Fact]
    public void Entities_Should_HavePrivateConstructor()
    {
        var entityTypes = Types
            .InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(Entity<>))
            .And()
            .AreNotAbstract()
            .GetTypes();

        var failingTypes = new List<Type>();

        foreach (var type in entityTypes)
        {
            var constructors = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
            if (!constructors.Any(c => c.IsPrivate))
                failingTypes.Add(type);
        }

        failingTypes
            .Should()
            .BeEmpty();

    }
}
