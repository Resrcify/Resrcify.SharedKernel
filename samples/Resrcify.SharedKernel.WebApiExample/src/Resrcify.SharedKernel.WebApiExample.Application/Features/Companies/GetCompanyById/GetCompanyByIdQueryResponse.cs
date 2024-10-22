using System;
using System.Collections.Generic;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.GetCompanyById;

public record GetCompanyByIdQueryResponse(
    Guid Id,
    string Name,
    string OrganizationNumber,
    IEnumerable<ContactDto> Contacts);

public record ContactDto(
    string FirstName,
    string LastName,
    string Email);