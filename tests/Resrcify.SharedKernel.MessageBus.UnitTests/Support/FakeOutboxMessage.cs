using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;

namespace Resrcify.SharedKernel.MessageBus.UnitTests.Support;

/// <summary>
/// Stands in for the outbox: <see cref="Handle"/> starts handling an outbox message (again, for a retry), and the IDs it
/// gives out are the same on every attempt at the same message, as the real outbox's are.
/// </summary>
internal sealed class FakeOutboxMessage : IOutboxMessageContext
{
    private readonly Dictionary<string, int> _issued = new(StringComparer.Ordinal);

    public Guid? MessageId { get; private set; }

    public Guid? NextStableId(string kind)
    {
        if (MessageId is not { } messageId)
            return null;
        var sequence = _issued.GetValueOrDefault(kind) + 1;
        _issued[kind] = sequence;
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{messageId:N}/{kind}/{sequence}")).AsSpan(0, 16));
    }

    /// <summary>An attempt at handling <paramref name="messageId"/>, until disposed.</summary>
    public IDisposable Handle(Guid messageId)
    {
        MessageId = messageId;
        _issued.Clear();
        return new Attempt(this);
    }

    private sealed class Attempt(FakeOutboxMessage outboxMessage) : IDisposable
    {
        public void Dispose()
            => outboxMessage.MessageId = null;
    }
}
