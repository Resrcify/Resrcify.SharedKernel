using System;

namespace Resrcify.SharedKernel.MessageBus.PublishSubscribe;

/// <summary>An event this service has a handler for, so subscribes to (registered once per event type).</summary>
internal sealed record SubscribedEvent(Type EventType);
