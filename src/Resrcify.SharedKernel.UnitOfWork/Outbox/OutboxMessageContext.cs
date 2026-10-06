using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Resrcify.SharedKernel.Abstractions.UnitOfWork;

namespace Resrcify.SharedKernel.UnitOfWork.Outbox;

/// <summary>
/// <see cref="IOutboxMessageContext"/>: the outbox message being processed on the current async flow. One instance
/// serves the whole service (a singleton, so anything can depend on it); the message itself flows with the work.
/// </summary>
internal sealed class OutboxMessageContext : IOutboxMessageContext
{
    private static readonly AsyncLocal<CurrentMessage?> Current = new();

    private OutboxMessageContext()
    {
    }

    public static OutboxMessageContext Instance { get; } = new();

    public Guid? MessageId => Current.Value?.MessageId;

    public Guid? NextStableId(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return Current.Value?.NextStableId(kind);
    }

    /// <summary>Makes <paramref name="messageId"/> the current outbox message until the returned scope is disposed.</summary>
    public static Scope Enter(Guid messageId)
    {
        var previous = Current.Value;
        Current.Value = new CurrentMessage(messageId);
        return new Scope(previous);
    }

    internal readonly struct Scope(CurrentMessage? previous) : IDisposable
    {
        public void Dispose()
            => Current.Value = previous;
    }

    internal sealed class CurrentMessage(Guid messageId)
    {
        private readonly Dictionary<string, int> _issued = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();

        public Guid MessageId => messageId;

        public Guid NextStableId(string kind)
        {
            int sequence;
            lock (_gate)
            {
                sequence = _issued.GetValueOrDefault(kind) + 1;
                _issued[kind] = sequence;
            }
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{messageId:N}/{kind}/{sequence}"));
            return new Guid(hash.AsSpan(0, 16));
        }
    }
}

/// <summary>Registers <see cref="IOutboxMessageContext"/>, from every way the outbox is set up.</summary>
internal static class OutboxMessageContextRegistration
{
    public static IServiceCollection AddOutboxMessageContext(this IServiceCollection services)
    {
        services.TryAddSingleton<IOutboxMessageContext>(OutboxMessageContext.Instance);
        return services;
    }
}
