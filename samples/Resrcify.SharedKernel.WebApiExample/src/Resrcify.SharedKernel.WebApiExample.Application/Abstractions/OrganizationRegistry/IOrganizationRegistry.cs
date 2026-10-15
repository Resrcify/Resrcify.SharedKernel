using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Results.Primitives;

namespace Resrcify.SharedKernel.WebApiExample.Application.Abstractions.OrganizationRegistry;

/// <summary>Another service: the registry that knows whether an organization number is registered.</summary>
public interface IOrganizationRegistry
{
    /// <summary>Whether <paramref name="organizationNumber"/> is registered; a failed result when the registry can't say.</summary>
    Task<Result<bool>> IsRegisteredAsync(
        string organizationNumber,
        CancellationToken cancellationToken = default);
}
