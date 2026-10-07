

using System;
using System.Linq;
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

    [Fact]
    public void FromName_ShouldBeNull_WhenTheNameIsNull()
    {
        ExampleEnumeration.FromName(null).ShouldBeNull();
        ExampleEnumeration.TryFromName(null, out var result).ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void Enumerations_ShouldListTheMembersInDeclarationOrder_WhenTheValuesAreSparseAndMany()
        => Flags.Enumerations.Keys.ShouldBe([1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048]);

    [Fact]
    public void Enumerations_ShouldListTheMembersInDeclarationOrder_WhenTheValuesAreDescending()
        => Descending.Enumerations.Values.Select(member => member.Name).ShouldBe(["High", "Middle", "Low"]);

    internal sealed class Flags : Enumeration<Flags>
    {
        public static readonly Flags F1 = new(1, "F1");
        public static readonly Flags F2 = new(2, "F2");
        public static readonly Flags F4 = new(4, "F4");
        public static readonly Flags F8 = new(8, "F8");
        public static readonly Flags F16 = new(16, "F16");
        public static readonly Flags F32 = new(32, "F32");
        public static readonly Flags F64 = new(64, "F64");
        public static readonly Flags F128 = new(128, "F128");
        public static readonly Flags F256 = new(256, "F256");
        public static readonly Flags F512 = new(512, "F512");
        public static readonly Flags F1024 = new(1024, "F1024");
        public static readonly Flags F2048 = new(2048, "F2048");

        private Flags(int value, string name) : base(value, name)
        {
        }
    }

    internal sealed class Descending : Enumeration<Descending>
    {
        public static readonly Descending High = new(300, "High");
        public static readonly Descending Middle = new(20, "Middle");
        public static readonly Descending Low = new(1, "Low");

        private Descending(int value, string name) : base(value, name)
        {
        }
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
