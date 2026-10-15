using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Results.Primitives;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.OrganizationRegistry;

namespace Resrcify.SharedKernel.WebApiExample.Infrastructure.OrganizationRegistry;

/// <summary>
/// The registry over HTTP. A timeout, an open circuit or a registry that can't be reached comes back from the
/// resilience pipeline as a failed result (<c>ToResultAsync</c>), never as an exception.
/// </summary>
internal sealed class OrganizationRegistryClient(HttpClient client)
    : IOrganizationRegistry
{
    public async Task<Result<bool>> IsRegisteredAsync(
        string organizationNumber,
        CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(
            new Uri($"organizations/{Uri.EscapeDataString(organizationNumber)}", UriKind.Relative),
            cancellationToken);
        return await response.ToResultAsync<bool>(cancellationToken: cancellationToken);
    }
}
