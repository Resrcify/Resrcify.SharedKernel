using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Resrcify.SharedKernel.UnitOfWork.Abstractions;

/// <summary>
/// Claims an outbox message for the transaction that processes it, so two service instances can't
/// process the same lane message at once.
/// </summary>
public interface IOutboxLaneClaim
{
    /// <summary>
    /// Runs first inside the processing transaction. Returns <see langword="false"/> when another
    /// instance holds the message (or it was processed meanwhile).
    /// </summary>
    Task<bool> TryClaimAsync(DbContext context, Guid messageId, CancellationToken cancellationToken);
}
