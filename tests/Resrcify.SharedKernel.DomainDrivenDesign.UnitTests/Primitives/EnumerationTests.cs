

using System;
using Xunit;
using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Shouldly;

namespace Resrcify.SharedKernel.DomainDrivenDesign.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public class EnumerationTests
{
    [Fact]
    public void FromValue_GivenValidValue_ReturnsCorrectEnumeration()
    {
        var result = ExampleEnumeration.FromValue(1);

        result.ShouldNotBeNull();
        result.ShouldBeEquivalentTo(ExampleEnumeration.Example1);
    }

    [Fact]
    public void FromValue_GivenInvalidValue_ReturnsNull()
    {
        var result = ExampleEnumeration.FromValue(999);

        result.ShouldBeNull();
    }

    [Fact]
    public void FromName_GivenValidName_ReturnsCorrectEnumeration()
    {
        var result = ExampleEnumeration.FromName("Example1");

        result.ShouldNotBeNull();
        result.ShouldBeEquivalentTo(ExampleEnumeration.Example1);
    }

    [Fact]
    public void FromName_GivenInvalidName_ReturnsNull()
    {
        var result = ExampleEnumeration.FromName("NonExistent");

        result.ShouldBeNull();
    }

    [Fact]
    public void Equals_GivenSameInstance_ReturnsTrue()
    {
        var instance = ExampleEnumeration.Example1;

        instance.Equals(instance).ShouldBeTrue();
    }

    [Fact]
    public void Equals_GivenSameValueDifferentInstance_ReturnsTrue()
    {
        var instance1 = ExampleEnumeration.Example1;
        var instance2 = ExampleEnumeration.Example1;

        instance1.Equals(instance2).ShouldBeTrue();
    }

    [Fact]
    public void Equals_GivenDifferentValue_ReturnsFalse()
    {
        var instance1 = ExampleEnumeration.Example1;
        var instance2 = ExampleEnumeration.Example2;

        instance1.Equals(instance2).ShouldBeFalse();
    }

    [Fact]
    public void GetHashCode_ReturnsConsistentResult()
    {
        var instance = ExampleEnumeration.Example1;
        var expectedHashCode = instance.Value.GetHashCode();

        instance.GetHashCode().ShouldBe(expectedHashCode);
    }

    [Fact]
    public void ToString_ReturnsCorrectName()
    {
        var instance = ExampleEnumeration.Example1;

        instance.ToString().ShouldBe("Example1");
    }

    internal sealed class ExampleEnumeration : Enumeration<ExampleEnumeration>
    {
        public static readonly ExampleEnumeration Example1 = new(1, "Example1");
        public static readonly ExampleEnumeration Example2 = new(2, "Example2");

        internal ExampleEnumeration(int value, string name) : base(value, name)
        {
        }
    }

    [Fact]
    public void FromName_ShouldIgnoreCase_WhenLookingUpAName()
        => ExampleEnumeration.FromName("EXAMPLE2").ShouldBe(ExampleEnumeration.Example2);

    [Fact]
    public void Enumerations_ShouldCountAnAliasOnce_WhenTwoFieldsHoldTheSameMember()
    {
        WithAlias.Enumerations.Count.ShouldBe(2);
        WithAlias.FromValue(1).ShouldBeSameAs(WithAlias.Red);
    }

    [Fact]
    public void FromValue_ShouldThrow_WhenTwoMembersShareAValue()
        => Should.Throw<InvalidOperationException>(() => DuplicateValue.FromValue(1))
            .Message.ShouldContain("more than one member with the value '1'");

    [Fact]
    public void FromName_ShouldThrow_WhenTwoMembersNamesDifferOnlyInCase()
        => Should.Throw<InvalidOperationException>(() => DuplicateName.FromName("Active"))
            .Message.ShouldContain("more than one member with the name");

    [Fact]
    public void FromName_ShouldWork_WhenAMemberIsInitializedFromAnotherMember()
    {
        SelfReferencing.Default.ShouldBe(SelfReferencing.Red);
        SelfReferencing.Enumerations.Count.ShouldBe(2);
        SelfReferencing.FromName("Blue").ShouldBe(SelfReferencing.Blue);
    }

    internal sealed class WithAlias : Enumeration<WithAlias>
    {
        public static readonly WithAlias Red = new(1, "Red");
        public static readonly WithAlias Blue = new(2, "Blue");
        public static readonly WithAlias Crimson = Red;

        private WithAlias(int value, string name) : base(value, name)
        {
        }
    }

    internal sealed class DuplicateValue : Enumeration<DuplicateValue>
    {
        public static readonly DuplicateValue First = new(1, "First");
        public static readonly DuplicateValue Second = new(1, "Second");

        private DuplicateValue(int value, string name) : base(value, name)
        {
        }
    }

    internal sealed class DuplicateName : Enumeration<DuplicateName>
    {
        public static readonly DuplicateName Active = new(1, "Active");
        public static readonly DuplicateName Shouting = new(2, "ACTIVE");

        private DuplicateName(int value, string name) : base(value, name)
        {
        }
    }

    internal sealed class SelfReferencing : Enumeration<SelfReferencing>
    {
        public static readonly SelfReferencing Red = new(1, "Red");
        public static readonly SelfReferencing Default = FromName("Red")!;
        public static readonly SelfReferencing Blue = new(2, "Blue");

        private SelfReferencing(int value, string name) : base(value, name)
        {
        }
    }
}
