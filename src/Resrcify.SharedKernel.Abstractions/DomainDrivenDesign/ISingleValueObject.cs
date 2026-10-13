using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

/// <summary>
/// A value object that wraps one value, stored as that value in one column: an id, a name, an ally code.
/// <para>
/// Implementing it is all a value object with the usual shape (<c>Value</c> and
/// <c>public static Result&lt;TSelf&gt; Create(TValue value)</c>) needs: the persistence convention
/// (<c>AddSingleValueObjectConversions</c> in <c>Resrcify.SharedKernel.UnitOfWork</c>) then maps every property of
/// the type as <see cref="Value"/>, so no <c>HasConversion</c> line is written per property.
/// </para>
/// </summary>
/// <typeparam name="TSelf">The value object itself.</typeparam>
/// <typeparam name="TValue">The type of the wrapped value, which is the type of the column.</typeparam>
public interface ISingleValueObject<TSelf, TValue>
    where TSelf : ISingleValueObject<TSelf, TValue>
{
    /// <summary>The wrapped value, written to the database as it is.</summary>
    TValue Value { get; }

    /// <summary>Makes the value object from a value, validating it.</summary>
    static abstract Result<TSelf> Create(TValue value);

    /// <summary>
    /// Makes the value object from a value read from the database.
    /// <para>
    /// By default it is <c>Create(value).Value</c>: the stored value is validated again, and a value that no longer
    /// passes throws an <see cref="System.InvalidOperationException"/> while the row is read, exactly as the
    /// hand-written <c>HasConversion(x =&gt; x.Value, v =&gt; X.Create(v).Value)</c> did. Implement it to read
    /// differently (e.g. to trust stored data), knowing that the read then no longer behaves as before.
    /// </para>
    /// </summary>
    static virtual TSelf FromPersisted(TValue value)
        => TSelf.Create(value).Value;
}
