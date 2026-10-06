using System;
using Resrcify.SharedKernel.Abstractions.DomainDrivenDesign;
using Resrcify.SharedKernel.DomainDrivenDesign.Primitives;

namespace Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;

/// <summary>
/// The requesting service's aggregate: asks for N pings, and stores what came back once gathered.
/// </summary>
internal sealed class PingRun : AggregateRoot<Guid>
{
    private PingRun(Guid id, int items) : base(id)
        => Items = items;

    public int Items { get; private set; }
    public int? Results { get; private set; }
    public int? Failures { get; private set; }
    public int? Unanswered { get; private set; }
    public DateTime? GatheredAtUtc { get; private set; }

    /// <param name="dedupGroup">Runs sharing a group can't be in flight at the same time (outbox dedup).</param>
    public static PingRun Start(int items, string? dedupGroup = null)
    {
        var run = new PingRun(Guid.NewGuid(), items);
        run.RaiseDomainEvent(new PingRequested(Guid.NewGuid(), run.Id, items, dedupGroup ?? run.Id.ToString("N")));
        return run;
    }

    public void RecordGathered(int results, int failures, int unanswered, DateTime now)
    {
        Results = results;
        Failures = failures;
        Unanswered = unanswered;
        GatheredAtUtc = now;
    }
}

/// <summary>A scatter-gather event: goes to the outbox's scatter-gather lane.</summary>
internal sealed record PingRequested(Guid Id, Guid RunId, int Items, string DedupGroup)
    : DomainEvent(Id), IDedupable
{
    public string DedupKey => DedupGroup;
}

/// <summary>A service's ordinary aggregate, whose event takes the regular outbox lane.</summary>
internal sealed class Note : AggregateRoot<Guid>
{
    private Note(Guid id) : base(id)
    {
    }

    public DateTime? HandledAtUtc { get; private set; }

    public static Note Create()
    {
        var note = new Note(Guid.NewGuid());
        note.RaiseDomainEvent(new NoteCreated(Guid.NewGuid(), note.Id));
        return note;
    }

    /// <summary>A note whose event is published to other services (as an integration event, from the outbox).</summary>
    public static Note CreateShared()
    {
        var note = new Note(Guid.NewGuid());
        note.RaiseDomainEvent(new NoteShared(Guid.NewGuid(), note.Id));
        return note;
    }

    public void MarkHandled(DateTime now)
        => HandledAtUtc = now;
}

internal sealed record NoteCreated(Guid Id, Guid NoteId) : DomainEvent(Id);

internal sealed record NoteShared(Guid Id, Guid NoteId) : DomainEvent(Id);
