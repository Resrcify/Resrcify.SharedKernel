using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Resrcify.SharedKernel.IntegrationTesting.GenericHostTestService;

/// <summary>Registers the services from the configuration it is given, as a generic-host service does.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "ServiceHostFixture<Startup> in the test project names it")]
public sealed class Startup(
    IConfiguration configuration)
{
    public void ConfigureServices(IServiceCollection services)
        => services.AddSingleton(new RegisteredSample(configuration["Sample:Value"]));

    public void Configure(IApplicationBuilder app)
        => app.Run(context => context.Response.WriteAsync(configuration["Sample:Value"] ?? string.Empty));
}
