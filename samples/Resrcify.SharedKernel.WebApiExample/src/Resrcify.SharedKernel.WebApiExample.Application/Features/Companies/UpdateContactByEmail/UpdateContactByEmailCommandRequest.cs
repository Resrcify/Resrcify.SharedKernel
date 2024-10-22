namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.UpdateContactByEmail;

public sealed record UpdateContactByEmailCommandRequest(
    string NewFirstName,
    string NewLastName,
    string Email);