using System;

using Resrcify.SharedKernel.Abstractions.UnitOfWork;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// Puts an event type in a named outbox lane: <c>services.AddSingleton&lt;IOutboxLaneEvent&gt;(new OutboxLaneEvent("slow", typeof(MyEvent)))</c>.
/// See <see cref="IOutboxLaneEvent"/>.
/// </summary>
/// <param name="Lane">The lane name.</param>
/// <param name="EventType">The domain event type the lane takes.</param>
public sealed record OutboxLaneEvent(string Lane, Type EventType) : IOutboxLaneEvent;
