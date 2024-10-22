using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Events;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.CompanyCreated;

internal sealed class CompanyCreatedEventHandler(
    ILogger<CompanyCreatedEventHandler> _logger)
    : IDomainEventHandler<CompanyCreatedEvent>
{
    public Task Handle(
        CompanyCreatedEvent notification,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Company created: {CompanyId}", notification.CompanyId);

        return Task.CompletedTask;
    }
}