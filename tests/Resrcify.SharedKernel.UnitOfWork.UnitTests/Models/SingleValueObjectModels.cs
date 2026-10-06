using System;
using System.Collections.Generic;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;

// Value objects shaped like the services' (a Value, a private constructor, Create validating), opted in by the
// interface alone.

internal sealed class PlayerId
    : ValueObject,
    ISingleValueObject<PlayerId, string>
{
    public const int MaxLength = 20;

    public string Value { get; }

    private PlayerId(string value)
        => Value = value;

    public static Result<PlayerId> Create(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith('P'))
            return Result.Failure<PlayerId>(Error.Validation("PlayerId.Invalid", $"'{value}' is not a player id."));
        if (value.Length > MaxLength)
            return Result.Failure<PlayerId>(Error.Validation("PlayerId.TooLong", $"'{value}' is too long."));
        return Result.Success(new PlayerId(value));
    }

    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }
}

internal sealed class AllyCode
    : ValueObject,
    ISingleValueObject<AllyCode, long>
{
    public long Value { get; }

    private AllyCode(long value)
        => Value = value;

    public static Result<AllyCode> Create(long value)
        => value is >= 100_000_000 and <= 999_999_999
            ? Result.Success(new AllyCode(value))
            : Result.Failure<AllyCode>(Error.Validation("AllyCode.OutOfRange", $"{value} is not an ally code."));

    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }
}

internal sealed class OrderNumber
    : ValueObject,
    ISingleValueObject<OrderNumber, int>
{
    public int Value { get; }

    private OrderNumber(int value)
        => Value = value;

    public static Result<OrderNumber> Create(int value)
        => value > 0
            ? Result.Success(new OrderNumber(value))
            : Result.Failure<OrderNumber>(Error.Validation("OrderNumber.NotPositive", $"{value} is not positive."));

    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }
}

internal sealed class TrackingId
    : ValueObject,
    ISingleValueObject<TrackingId, Guid>
{
    public Guid Value { get; }

    private TrackingId(Guid value)
        => Value = value;

    public static Result<TrackingId> Create(Guid value)
        => value != Guid.Empty
            ? Result.Success(new TrackingId(value))
            : Result.Failure<TrackingId>(Error.Validation("TrackingId.Empty", "The tracking id is empty."));

    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }
}

/// <summary>Reads stored values without validating them: its own <c>FromPersisted</c>.</summary>
internal sealed class TrustedCode
    : ValueObject,
    ISingleValueObject<TrustedCode, string>
{
    public string Value { get; }

    private TrustedCode(string value)
        => Value = value;

    public static Result<TrustedCode> Create(string value)
        => value.Length == 3
            ? Result.Success(new TrustedCode(value))
            : Result.Failure<TrustedCode>(Error.Validation("TrustedCode.Length", $"'{value}' is not three long."));

    public static TrustedCode FromPersisted(string value)
        => new(value);

    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }
}

/// <summary>The same shape, not opted in: the convention must leave it alone.</summary>
internal sealed class LegacyCode
    : ValueObject
{
    public string Value { get; }

    private LegacyCode(string value)
        => Value = value;

    public static Result<LegacyCode> Create(string value)
        => Result.Success(new LegacyCode(value));

    public override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }
}

internal sealed class VoPlayer(
    PlayerId id,
    AllyCode allyCode)
{
    public PlayerId Id { get; private set; } = id;
    public AllyCode AllyCode { get; set; } = allyCode;
    public AllyCode? FormerAllyCode { get; set; }
    public PlayerId? SimulatedPlayerId { get; set; }
    public TrustedCode? Code { get; set; }
    public LegacyCode? Legacy { get; set; }
    public VoAddress Address { get; set; } = new();
    public VoStats Stats { get; set; } = new();
    public List<PlayerId> Friends { get; set; } = [];
    public List<VoMembership> Memberships { get; } = [];
}

/// <summary>Owned by <see cref="VoPlayer"/>.</summary>
internal sealed class VoAddress
{
    public string Street { get; set; } = "Main";
    public PlayerId? ForwardTo { get; set; }
}

/// <summary>A complex type of <see cref="VoPlayer"/>.</summary>
internal sealed class VoStats
{
    public int Level { get; set; }
    public AllyCode? Referrer { get; set; }
}

/// <summary>A composite key of two value objects, one of them a foreign key, and an alternate key.</summary>
internal sealed class VoMembership(
    OrderNumber number,
    PlayerId playerId,
    TrackingId trackingId)
{
    public OrderNumber Number { get; private set; } = number;
    public PlayerId PlayerId { get; private set; } = playerId;
    public TrackingId TrackingId { get; private set; } = trackingId;
}

/// <summary>Keys that EF Core could generate (int, Guid), left to its conventions.</summary>
internal sealed class VoTicket(
    OrderNumber id,
    TrackingId tracking)
{
    public OrderNumber Id { get; private set; } = id;
    public TrackingId Tracking { get; private set; } = tracking;
}

internal sealed class VoBadge(
    TrackingId id)
{
    public TrackingId Id { get; private set; } = id;
}

/// <summary>
/// Opted in for two value types, which can't be stored. Generic so that scanning this assembly skips it; a test
/// closes it and hands it to the convention.
/// </summary>
internal sealed class TwiceStored<TTag>
    : ISingleValueObject<TwiceStored<TTag>, string>,
    ISingleValueObject<TwiceStored<TTag>, int>
{
    public string Value { get; } = "1";

    int ISingleValueObject<TwiceStored<TTag>, int>.Value => 1;

    public static Result<TwiceStored<TTag>> Create(string value)
        => Result.Success(new TwiceStored<TTag>());

    public static Result<TwiceStored<TTag>> Create(int value)
        => Result.Success(new TwiceStored<TTag>());
}
