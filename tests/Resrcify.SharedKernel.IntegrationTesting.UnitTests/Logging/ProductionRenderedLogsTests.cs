using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Resrcify.SharedKernel.IntegrationTesting.Logging;
using Serilog.Events;
using Serilog.Parsing;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.UnitTests.Logging;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ProductionRenderedLogsTests
{
    private static readonly MessageTemplateParser Parser = new();

    [Fact]
    public void Emit_ShouldQuoteAStringProperty_AsProductionsConsolePrintsIt()
    {
        var logs = new ProductionRenderedLogs();

        logs.Emit(Event("Sync of {Shard} failed {Attempts} times", ("Shard", "alpha"), ("Attempts", 3)));

        var line = logs.Lines.ShouldHaveSingleItem();
        line.Message.ShouldBe("Sync of \"alpha\" failed 3 times");
        line.Template.ShouldBe("Sync of {Shard} failed {Attempts} times");
        line.Level.ShouldBe(LogEventLevel.Warning);
    }

    [Fact]
    public void Emit_ShouldPrintAStringPropertyAsItIs_WhenTheTemplateSaysLiteral()
    {
        var logs = new ProductionRenderedLogs();

        logs.Emit(Event("Sync of {Shard:l} failed", ("Shard", "alpha")));

        logs.Lines.ShouldHaveSingleItem().Message.ShouldBe("Sync of alpha failed");
    }

    [Fact]
    public void Clear_ShouldForgetTheLinesSoFar()
    {
        var logs = new ProductionRenderedLogs();
        logs.Emit(Event("first"));

        logs.Clear();
        logs.Emit(Event("second"));

        logs.Lines.Select(line => line.Message).ShouldBe(["second"]);
    }

    private static LogEvent Event(
        string template,
        params (string Name, object Value)[] properties)
        => new(
            DateTimeOffset.UnixEpoch,
            LogEventLevel.Warning,
            exception: null,
            Parser.Parse(template),
            properties.Select(property => new LogEventProperty(property.Name, new ScalarValue(property.Value))));
}
