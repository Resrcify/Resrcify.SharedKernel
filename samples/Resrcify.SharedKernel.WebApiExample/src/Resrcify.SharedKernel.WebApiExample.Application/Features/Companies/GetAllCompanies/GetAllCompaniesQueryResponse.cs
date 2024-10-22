using System;
using System.Collections.Generic;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.GetAllCompanies;

public sealed record GetAllCompaniesQueryResponse(IEnumerable<CompanyDto> Companies);

public sealed record CompanyDto(
    Guid Id,
    string Name,
    string OrganizationNumber);