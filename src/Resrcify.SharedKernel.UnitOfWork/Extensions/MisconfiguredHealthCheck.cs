using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Resrcify.SharedKernel.UnitOfWork.Extensions;

/// <summary>
/// A health check that can't run as registered (a registration it needs is missing): it reports why, with the
/// registration's failure status, instead of throwing (a 500 on the health endpoint). The host's start-up validation
/// catches the same mistake first; this is for a container used without a host.
/// </summary>
internal sealed class MisconfiguredHealthCheck(string description)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus, description));
    }
}
