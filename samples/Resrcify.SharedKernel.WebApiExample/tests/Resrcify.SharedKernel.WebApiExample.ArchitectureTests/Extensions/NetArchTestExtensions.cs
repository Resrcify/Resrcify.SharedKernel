using NetArchTest.Rules;
using Shouldly;

namespace Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Extensions;

public static class NetArchTestExtensions
{
    public static void Evaluate(this ConditionList conditionList)
    {
        var failingTypeNames = conditionList
            .GetResult().FailingTypeNames
            ?? [];
        failingTypeNames
            .ShouldBeEmpty();
    }
}