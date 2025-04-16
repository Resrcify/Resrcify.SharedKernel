using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetArchTest.Rules;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.ResultFramework.Primitives;
using Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Extensions;
using Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Helpers;
using Shouldly;

namespace Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Tests;

public class ApplicationTests : BaseTest
{

    [Fact]
    public void CommandHandlers_Should_HaveCommandHandlerPostFix()
        => Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Should()
            .HaveNameEndingWith("CommandHandler")
            .Evaluate();

    [Fact]
    public void CommandHandlers_Should_BeSealed()
        => Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Should()
            .BeSealed()
            .Evaluate();

    [Fact]
    public void CommandHandlers_Should_ReturnResult()
    {
        var types = Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<>))
            .GetTypes();

        var failureTypes = new List<Type>();
        foreach (var type in types)
        {
            var handler = type.GetMethod("Handle");

            if (handler is null)
                failureTypes.Add(type);

            if (handler!.ReturnType.Name != typeof(Task<>).Name &&
                handler.ReturnType.Name != typeof(Task).Name &&
                handler.ReturnType.Name != typeof(Result).Name &&
                handler.ReturnType.Name != typeof(Result<>).Name)
                failureTypes.Add(type);

            var genArguments = handler!.ReturnType.GetGenericArguments();

            foreach (var genArgument in genArguments)
                if (genArgument.Name != typeof(Result).Name &&
                    genArgument.Name != typeof(Result<>).Name)
                    failureTypes.Add(type);
        }

        failureTypes
            .ShouldBeEmpty();
    }

    [Fact]
    public void CommandResponses_Should_HaveResponsePostFix()
    {
        var types = Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommand<>))
            .And()
            .DoNotHaveNameMatching("InterceptRequestCommand")
            .GetTypes();

        var failureTypes = new List<Type>();
        foreach (var type in types)
        {
            var interfaces = type.GetInterfaces();
            if (interfaces.Length == 0)
            {
                failureTypes.Add(type);
                continue;
            }
            var firstLevelImplementation = interfaces[0];
            var genericArgs = firstLevelImplementation.GetGenericArguments();

            if (genericArgs.Length == 0 || genericArgs.Length > 1)
            {
                failureTypes.Add(type);
                continue;
            }

            var arg = genericArgs[0];

            if (!arg.Name.EndsWith("Response"))
                failureTypes.Add(type);
        }

        failureTypes
            .ShouldBeEmpty();
    }

    [Fact]
    public void Commands_Should_HaveCommandPostFix()
        => Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommand<>))
            .Or()
            .ImplementInterface(typeof(ICommand))
            .Should()
            .HaveNameEndingWith("Command")
            .Evaluate();

    [Fact]
    public void Commands_Should_BeSealed()
        => Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommand<>))
            .Or()
            .ImplementInterface(typeof(ICommand))
            .Should()
            .BeSealed()
            .Evaluate();

    [Fact]
    public void QueryHandlers_Should_HaveQueryHandlerPostFix()
        => Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .HaveNameEndingWith("QueryHandler")
            .Evaluate();

    [Fact]
    public void QueryHandlers_Should_BeSealed()
        => Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .BeSealed()
            .Evaluate();

    [Fact]
    public void QueryHandlers_Should_ReturnResult()
    {
        var types = Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .GetTypes();

        var failureTypes = new List<Type>();
        foreach (var type in types)
        {
            var handler = type.GetMethod("Handle");

            if (handler is null)
                failureTypes.Add(type);

            if (handler!.ReturnType.Name != typeof(Task<>).Name &&
                handler.ReturnType.Name != typeof(Task).Name &&
                handler.ReturnType.Name != typeof(Result).Name &&
                handler.ReturnType.Name != typeof(Result<>).Name)
                failureTypes.Add(type);

            var genArguments = handler!.ReturnType.GetGenericArguments();

            foreach (var genArgument in genArguments)
                if (genArgument.Name != typeof(Result).Name &&
                    genArgument.Name != typeof(Result<>).Name)
                    failureTypes.Add(type);
        }

        failureTypes
            .ShouldBeEmpty();
    }

    [Fact]
    public void QueryResponses_Should_HaveResponsePostFix()
    {
        var types = Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQuery<>))
            .GetTypes();

        var failureTypes = new List<Type>();
        foreach (var type in types)
        {
            var interfaces = type.GetInterfaces();
            if (interfaces.Length == 0)
            {
                failureTypes.Add(type);
                continue;
            }
            var firstLevelImplementation = interfaces[0];
            var genericArgs = firstLevelImplementation.GetGenericArguments();

            if (genericArgs.Length == 0 || genericArgs.Length > 1)
            {
                failureTypes.Add(type);
                continue;
            }

            var arg = genericArgs[0];

            if (!arg.Name.EndsWith("Response"))
                failureTypes.Add(type);
        }

        failureTypes
            .ShouldBeEmpty();
    }

    [Fact]
    public void Queries_Should_BeSealed()
        => Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQuery<>))
            .Should()
            .BeSealed()
            .Evaluate();

    [Fact]
    public void Queries_Should_HaveQueryPostFix()
        => Types
            .InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IQuery<>))
            .Should()
            .HaveNameEndingWith("Query")
            .Evaluate();
}
