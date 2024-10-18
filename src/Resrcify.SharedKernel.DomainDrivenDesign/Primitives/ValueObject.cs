using System.Linq;
using System;
using System.Collections.Generic;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
public abstract class ValueObject : IEquatable<ValueObject>
{
    public abstract IEnumerable<object> GetAtomicValues();

    private bool ValuesAreEqual(ValueObject other)
    {
        return GetAtomicValues()
            .SequenceEqual(other.GetAtomicValues());
    }

    public override string ToString()
        => string.Join(", ", GetAtomicValues());

    public override bool Equals(object? obj)
    {
        return obj is ValueObject other &&
            ValuesAreEqual(other);
    }

    public override int GetHashCode()
    {
        return GetAtomicValues()
            .Aggregate(default(int), HashCode.Combine);
    }

    public bool Equals(ValueObject? other)
    {
        return other is not null &&
            ValuesAreEqual(other);
    }

    public static bool operator ==(ValueObject? left, ValueObject? right)
    {
        if (left is null || right is null)
            return false;

        if (ReferenceEquals(left, right))
            return true;

        return left.Equals(right);
    }

    public static bool operator !=(ValueObject? left, ValueObject? right)
        => !(left == right);
}