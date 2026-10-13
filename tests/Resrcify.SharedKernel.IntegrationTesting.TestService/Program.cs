using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Resrcify.SharedKernel.IntegrationTesting.TestService;

var builder = WebApplication.CreateBuilder(args);

// Read while registering, before the host is built: where the services bind their Database and MessageBus sections.
builder.Services.AddSingleton(new RegistrationSettings(
    builder.Configuration
        .AsEnumerable()
        .Where(setting => setting.Value is not null)
        .ToDictionary(setting => setting.Key, setting => setting.Value)));
builder.Services.AddSingleton<IGreeter, Greeter>();

var app = builder.Build();

app.MapGet("/greeting", (IGreeter greeter) => greeter.Greet());
app.MapGet("/database", async (IConfiguration configuration) =>
{
    var database = configuration.GetSection("Database");
    var connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = database["Host"],
        Port = int.Parse(database["Port"]!, System.Globalization.CultureInfo.InvariantCulture),
        Database = database["Database"],
        Username = database["Username"],
        Password = database["Password"],
    };
    await using var connection = new NpgsqlConnection(connectionString.ConnectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT current_database()", connection);
    return (string?)await command.ExecuteScalarAsync();
});

await app.RunAsync();

/// <summary>The entry point, public so that the tests' <c>ServiceHostFixture&lt;Program&gt;</c> can name it.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "WebApplicationFactory<Program> in the test project needs the entry point public")]
public partial class Program
{
    protected Program()
    {
    }
}
