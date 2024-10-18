using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Resrcify.SharedKernel.WebApiExample.Infrastructure;
using Resrcify.SharedKernel.WebApiExample.Persistence;
using Resrcify.SharedKernel.WebApiExample.Application;
using Resrcify.SharedKernel.WebApiExample.Presentation;
using Serilog;

namespace Resrcify.SharedKernel.WebApiExample.Web;

public class Startup(IConfiguration configuration)
{
    public IConfiguration Configuration { get; } = configuration;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddPresentationServices();
        services.AddApplicationServices();
        services.AddInfrastructureServices();
        services.AddPersistanceServices(Configuration);
    }

    public static void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Resrcify.WebApiExample v1"));
        }

        app.UseSerilogRequestLogging();
        app.UseRouting();
        app.UseCors("WebApiExampleCors");
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
            endpoints.MapControllers());
    }
}