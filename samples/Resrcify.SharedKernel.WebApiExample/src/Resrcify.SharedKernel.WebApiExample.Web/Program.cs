using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Resrcify.SharedKernel.Observability.Extensions;
using Resrcify.SharedKernel.Web.Extensions;
using Resrcify.SharedKernel.WebApiExample.Application;
using Resrcify.SharedKernel.WebApiExample.Infrastructure;
using Resrcify.SharedKernel.WebApiExample.Persistence;
using Resrcify.SharedKernel.WebApiExample.Presentation;

// The canonical SharedKernel 4.3 host: every layer registers itself from the host's configuration (no IConfiguration
// injected anywhere: options are bound and validated on start).
var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddPresentationServices()
    .AddApplicationServices()
    .AddInfrastructureServices(builder.Configuration)
    .AddPersistenceServices(builder.Configuration);

var app = builder.Build();

// First: one line per request (the probes and the scrape left out), then the exception handler inside it, so the line
// logs the status the handler answered (problem details for an unhandled exception, AddResultProblemDetails; a 499
// for a client that gave up) and the exception's stack is logged once.
app.UseServiceDefaults();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapEndpoints();          // every IEndpoint of the Presentation layer
app.MapHealthEndpoints();    // /health, /health/ready, /health/live
app.MapServiceMetrics();     // /metrics, for Prometheus

await app.RunAsync();
