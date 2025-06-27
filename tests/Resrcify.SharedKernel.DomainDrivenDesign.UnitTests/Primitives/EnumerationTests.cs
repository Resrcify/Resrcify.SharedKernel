

using Xunit;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Shouldly;

namespace Resrcify.SharedKernel.DomainDrivenDesign.UnitTests.Primitives;

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
}