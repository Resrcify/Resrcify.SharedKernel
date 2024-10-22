using System;
using Resrcify.SharedKernel.Messaging.Abstractions;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.GetAllCompanies;

public sealed record GetAllCompaniesQuery : ICachingQuery<GetAllCompaniesQueryResponse>
{
    public string? CacheKey { get; set; } = "Companies";
    public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(5);
}
