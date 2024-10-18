using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Resrcify.SharedKernel.WebApiExample.Web;

public class Program
{
    public static void Main(string[] args)
        => CreateHostBuilder(args)
            .UseSerilog((context, configuration) =>
                configuration.ReadFrom.Configuration(context.Configuration))
            .Build()
            .Run();

    public static IHostBuilder CreateHostBuilder(string[] args)
        => Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls("http://*:11000");
                webBuilder.UseStartup<Startup>();
            });
}
