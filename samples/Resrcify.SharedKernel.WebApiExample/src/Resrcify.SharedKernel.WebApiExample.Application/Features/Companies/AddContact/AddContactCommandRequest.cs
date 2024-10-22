namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.AddContact;

public sealed record AddContactCommandRequest(
    string FirstName,
    string LastName,
    string Email);