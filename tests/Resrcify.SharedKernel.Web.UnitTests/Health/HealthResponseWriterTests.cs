using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Resrcify.SharedKernel.Web.Health;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Web.UnitTests.Health;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class HealthResponseWriterTests
{
    [Fact]
    public async Task WriteAsync_ShouldWriteTheOverallStatusAndDuration()
    {
        var json = await WriteAsync(Report());

        json.GetProperty("status").GetString().ShouldBe("Unhealthy");
        json.GetProperty("duration").GetString().ShouldBe("00:00:00.0150000");
    }

    [Fact]
    public async Task WriteAsync_ShouldWriteEachChecksStatusDescriptionDataErrorAndTags()
    {
        var json = await WriteAsync(Report());

        var database = json.GetProperty("checks").GetProperty("database");
        database.GetProperty("status").GetString().ShouldBe("Unhealthy");
        database.GetProperty("description").GetString().ShouldBe("Connection refused");
        database.GetProperty("duration").GetString().ShouldBe("00:00:00.0120000");
        database.GetProperty("data").GetProperty("attempts").GetInt32().ShouldBe(3);
        database.GetProperty("data").GetProperty("host").GetString().ShouldBe("postgres");
        database.GetProperty("error").GetString().ShouldBe("boom");
        database.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ShouldBe(["ready"]);
    }

    [Fact]
    public async Task WriteAsync_ShouldWriteNulls_WhenACheckHasNoDescriptionOrError()
    {
        var json = await WriteAsync(Report());

        var jobs = json.GetProperty("checks").GetProperty("jobs");
        jobs.GetProperty("status").GetString().ShouldBe("Healthy");
        jobs.GetProperty("description").ValueKind.ShouldBe(JsonValueKind.Null);
        jobs.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
        jobs.GetProperty("data").EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task WriteAsync_ShouldAnswerJson()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthResponseWriter.WriteAsync(context, Report());

        context.Response.ContentType.ShouldBe("application/json; charset=utf-8");
    }

    private static HealthReport Report()
        => new(
            new Dictionary<string, HealthReportEntry>
            {
                ["database"] = new(
                    HealthStatus.Unhealthy,
                    "Connection refused",
                    TimeSpan.FromMilliseconds(12),
                    new InvalidOperationException("boom"),
                    new Dictionary<string, object> { ["attempts"] = 3, ["host"] = "postgres" },
                    ["ready"]),
                ["jobs"] = new(
                    HealthStatus.Healthy,
                    description: null,
                    TimeSpan.FromMilliseconds(1),
                    exception: null,
                    data: null),
            },
            HealthStatus.Unhealthy,
            TimeSpan.FromMilliseconds(15));

    private static async Task<JsonElement> WriteAsync(
        HealthReport report)
    {
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await HealthResponseWriter.WriteAsync(context, report);

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);
        return document.RootElement.Clone();
    }
}
