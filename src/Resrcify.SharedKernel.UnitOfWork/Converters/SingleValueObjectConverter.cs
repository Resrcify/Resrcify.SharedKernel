using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;

namespace Resrcify.SharedKernel.UnitOfWork.Converters;

/// <summary>
/// Stores a single-value value object as its <see cref="ISingleValueObject{TSelf, TValue}.Value"/> and reads it back
/// with <see cref="ISingleValueObject{TSelf, TValue}.FromPersisted"/> (by default <c>Create(value).Value</c>).
/// </summary>
/// <remarks>
/// <para>
/// The same conversion as the hand-written <c>HasConversion(x =&gt; x.Value, v =&gt; X.Create(v).Value)</c>: EF Core
/// never hands it a <see langword="null"/> (a null property is a NULL column), and it sets no mapping hints, so a
/// column keeps the type, length and nullability its property configuration gives it. Registered for every property
/// of an opted-in type by <c>AddSingleValueObjectConversions</c>; usable on its own with
/// <c>HasConversion&lt;SingleValueObjectConverter&lt;X, TValue&gt;&gt;()</c>.
/// </para>
/// <para>
/// It works in a compiled model (<c>dotnet ef dbcontext optimize</c>): the model creates it with its public
/// parameterless constructor, and the model for NativeAOT and precompiled queries (<c>--native-aot</c>,
/// <c>--precompile-queries</c>), which writes the conversion out as C#, gets
/// <c>((ISingleValueObject&lt;X, TValue&gt;)v).Value</c> and <see cref="SingleValueObjectConverter.FromPersisted"/>,
/// both public.
/// </para>
/// </remarks>
public sealed class SingleValueObjectConverter<TSelf, TValue>
    : ValueConverter<TSelf, TValue>
    where TSelf : ISingleValueObject<TSelf, TValue>
{
    public SingleValueObjectConverter()
        : base(
            valueObject => valueObject.Value,
            value => SingleValueObjectConverter.FromPersisted<TSelf, TValue>(value))
    {
    }

    /// <summary>
    /// Every instance converts alike, so all are equal. EF Core makes one per property, and compares the converters
    /// of two mappings to tell whether they are the same column read the same way: an owned type's key and its
    /// owner's key, for instance, are projected once only when their converters are equal (as they were when the
    /// owned key borrowed the owner's hand-written converter).
    /// </summary>
    public override bool Equals(object? obj)
        => obj is SingleValueObjectConverter<TSelf, TValue>;

    public override int GetHashCode()
        => typeof(SingleValueObjectConverter<TSelf, TValue>).GetHashCode();
}

/// <summary>The read <see cref="SingleValueObjectConverter{TSelf, TValue}"/> converts with.</summary>
public static class SingleValueObjectConverter
{
    /// <summary>
    /// <typeparamref name="TSelf"/>'s <see cref="ISingleValueObject{TSelf, TValue}.FromPersisted"/>: by default
    /// <c>Create(value).Value</c>, which throws on a stored value that no longer validates.
    /// </summary>
    /// <remarks>
    /// The converter's read expression calls this method because an expression tree can't call a static abstract or
    /// virtual interface member. It is public because a compiled model for NativeAOT, and precompiled queries, write
    /// that expression out as C# in the service's own assembly.
    /// </remarks>
    /// <param name="value">The value read from the column.</param>
    public static TSelf FromPersisted<TSelf, TValue>(TValue value)
        where TSelf : ISingleValueObject<TSelf, TValue>
        => TSelf.FromPersisted(value);
}
