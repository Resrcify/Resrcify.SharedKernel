using System;
using FluentAssertions;
using NetArchTest.Rules;

namespace Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Extensions;

public static class NetArchTestExtensions
{
    public static void Evaluate(this ConditionList conditionList)
    {
        var failingTypeNames = conditionList
            .GetResult().FailingTypeNames
            ?? [];
        failingTypeNames
            .Should()
            .BeEmpty();
    }
}