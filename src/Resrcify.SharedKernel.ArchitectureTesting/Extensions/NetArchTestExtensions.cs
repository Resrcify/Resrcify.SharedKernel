using NetArchTest.Rules;
using Shouldly;

namespace Resrcify.SharedKernel.ArchitectureTesting.Extensions;

/// <summary>
/// Asserts a NetArchTest <see cref="ConditionList"/> evaluates to no failing
/// types. Replaces the duplicated <c>NetArchTestExtensions.Evaluate</c>
/// helper that lived in every consumer's <c>*.ArchitectureTests/Extensions</c>.
/// </summary>
public static class NetArchTestExtensions
{
    public static void Evaluate(this ConditionList conditionList)
    {
        var failing = conditionList.GetResult().FailingTypeNames ?? [];
        failing.ShouldBeEmpty();
    }
}
