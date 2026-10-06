using System.Linq;
using System;
using System.Collections.Generic;

namespace Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

public abstract class ValueObject
    : IEquatable<ValueObject>
{
    public abstract IEnumerable<object> GetAtomicValues();

    private bool ValuesAreEqual(
        ValueObject other)
    {
        return GetAtomicValues()
            .SequenceEqual(other.GetAtomicValues());
    }

    public override string ToString()
        => string.Join(", ", GetAtomicValues());

    public override bool Equals(
        object? obj)
        => obj is ValueObject other &&
            Equals(other);

    /// <summary>Equal when both are the same type of value object with the same atomic values.</summary>
    public bool Equals(
        ValueObject? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return other.GetType() == GetType() &&
            ValuesAreEqual(other);
    }

    public override int GetHashCode()
    {
        return GetAtomicValues()
            .Aggregate(
                GetType().GetHashCode(),
                HashCode.Combine);
    }

    public static bool operator ==(
        ValueObject? left,
        ValueObject? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.Equals(right);
    }

    public static bool operator !=(
        ValueObject? left,
        ValueObject? right)
        => !(left == right);
}