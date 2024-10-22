using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Messaging.Abstractions;
using Resrcify.SharedKernel.WebApiExample.Domain.Features.Companies.Events;

namespace Resrcify.SharedKernel.WebApiExample.Application.Features.Companies.CompanyNameUpdated;

internal sealed class CompanyNameUpdatedEventHandler(
    ILogger<CompanyNameUpdatedEventHandler> _logger)
    : IDomainEventHandler<CompanyNameUpdatedEvent>
{
    public Task Handle(
        CompanyNameUpdatedEvent notification,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Company name updated: {CompanyId}", notification.CompanyId);

        return Task.CompletedTask;
    }
}