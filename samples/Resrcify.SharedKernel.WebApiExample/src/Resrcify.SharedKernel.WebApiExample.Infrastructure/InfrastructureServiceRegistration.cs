using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz;
using Resrcify.SharedKernel.Caching.Extensions;
using Resrcify.SharedKernel.Observability.Extensions;
using Resrcify.SharedKernel.UnitOfWork.BackgroundJobs;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.WebApiExample.Application.Abstractions.OrganizationRegistry;
using Resrcify.SharedKernel.WebApiExample.Infrastructure.OrganizationRegistry;

namespace Resrcify.SharedKernel.WebApiExample.Infrastructure;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // OpenTelemetry (ASP.NET Core, HttpClient, the runtime, every SharedKernel source and meter, Quartz, Npgsql),
        // the Prometheus exporter, OTLP when Observability:OtlpEndpoint is set, and Serilog from the Serilog section.
        services.AddServiceTelemetry("webapiexample", configuration);

        // The in-memory distributed cache, ICachingService and IClaimStore (the caching and idempotency behaviors').
        services.AddDistributedCaching();

        // Quartz runs the outbox job (registered by AddOutboxProcessing in Persistence); heartbeats feed the liveness check.
        services.AddQuartz(quartz => quartz.AddJobHeartbeats());
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
        services.AddHealthChecks().AddQuartzJobs();   // tagged live

        services.AddOrganizationRegistry();
        return services;
    }

    // Another service, called through the result-pattern resilience preset: its options come from the
    // OrganizationRegistry:Resilience section and are validated when the host starts, and HttpClient's own timeout is
    // kept above the total timeout.
    private static void AddOrganizationRegistry(this IServiceCollection services)
    {
        services
            .AddOptions<OrganizationRegistryOptions>()
            .BindConfiguration(OrganizationRegistryOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddHttpClient<IOrganizationRegistry, OrganizationRegistryClient>((provider, client) =>
                client.BaseAddress = provider.GetRequiredService<IOptions<OrganizationRegistryOptions>>().Value.Url)
            .AddResultResilience($"{OrganizationRegistryOptions.SectionName}:Resilience");
    }
}
