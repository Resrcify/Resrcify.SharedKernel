using System;
using Resrcify.SharedKernel.Messaging.Abstractions;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.GetCompanyById;

public sealed record GetCompanyByIdQuery(
    Guid CompanyId)
    : ICachingQuery<GetCompanyByIdQueryResponse>
{
    public string? CacheKey { get; set; } = $"Company-{CompanyId}";
    public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(5);
}
