using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Resrcify.SharedKernel.Observability.Extensions;
using Resrcify.SharedKernel.Observability.Logging;
using Resrcify.SharedKernel.Observability.UnitTests.Support;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Shouldly;
using Xunit;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Resrcify.SharedKernel.Observability.UnitTests.Logging;

/// <summary>
/// Each host logs through its own Serilog logger, also with other hosts in the process (a test's), and stopping one
/// never closes another's. The first host's logger is still Serilog's static <see cref="Log.Logger"/>, as with one host
/// before. In <see cref="TelemetryCollection"/>: the static logger is the process', so no other test may build a host
/// meanwhile.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
[Collection(TelemetryCollection.Name)]
public sealed partial class HostLoggerFactoryTests
{
    private const string WriterProperty = "Writer";
    private const string HostProperty = "Host";

    [Fact]
    public void CreateLogger_ShouldWriteAsItsOwnHost_WhenAnotherHostWasBuiltAfterIt()
    {
        // A logger a host makes after another host started (a request handler's, made at its first request) wrote
        // through the static Log.Logger of that moment: the other host's.
        using var first = TestHost.Build("first");
        using var second = TestHost.Build("second");

        var firstLogger = first.CreateLogger();
        var secondLogger = second.CreateLogger();
        LogHello(firstLogger, "first");
        LogHello(secondLogger, "second");

        first.Writers().ShouldBe(["first"]);
        second.Writers().ShouldBe(["second"]);
    }

    [Fact]
    public void Dispose_ShouldLeaveTheOtherHostLogging_WhenTheHostBuiltLastStops()
    {
        using var first = TestHost.Build("first");
        var second = TestHost.Build("second");
        var madeBefore = first.CreateLogger();

        second.Dispose();
        var madeAfter = first.CreateLogger();
        LogHello(madeBefore, "first");
        LogHello(madeAfter, "first");

        first.Sink.IsDisposed.ShouldBeFalse();
        first.Writers().ShouldBe(["first", "first"]);
        second.Sink.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public void Dispose_ShouldLeaveTheOtherHostLogging_WhenTheHostBuiltFirstStops()
    {
        // The first host's stop closed the static logger (Log.CloseAndFlush), which was the second host's.
        var first = TestHost.Build("first");
        using var second = TestHost.Build("second");
        var madeBefore = second.CreateLogger();

        first.Dispose();
        var madeAfter = second.CreateLogger();
        LogHello(madeBefore, "second");
        LogHello(madeAfter, "second");

        second.Sink.IsDisposed.ShouldBeFalse();
        second.Writers().ShouldBe(["second", "second"]);
        first.Sink.IsDisposed.ShouldBeTrue();
        first.Writers().ShouldBeEmpty();
    }

    [Fact]
    public void Log_ShouldWriteAsTheHost_WhenItIsTheOnlyOne()
    {
        // As before: code logging through Serilog's static API writes as the host, until the host stops.
        var host = TestHost.Build("only");

        Log.ForContext(WriterProperty, "static").Information("Hello from the static logger");
        host.Dispose();

        host.Writers().ShouldBe(["static"]);
        Log.Logger.ShouldBeSameAs(Logger.None);
    }

    [Fact]
    public void Log_ShouldWriteAsTheFirstHost_WhileItRuns()
    {
        var first = TestHost.Build("first");
        using var second = TestHost.Build("second");

        Log.ForContext(WriterProperty, "static").Information("Hello from the static logger");
        first.Dispose();
        using var third = TestHost.Build("third");
        Log.ForContext(WriterProperty, "static").Information("Hello from the static logger");

        first.Writers().ShouldBe(["static"]);
        second.Writers().ShouldBeEmpty();
        third.Writers().ShouldBe(["static"]);   // the first one stopped: the next host built takes the static logger
        third.Factory.OwnsStaticLogger.ShouldBeTrue();
    }

    [Fact]
    public void Dispose_ShouldLeaveTheStaticLoggerAlone_WhenSomethingElseReplacedIt()
    {
        var host = TestHost.Build("only");
        using var replacement = new LoggerConfiguration().CreateLogger();
        Log.Logger = replacement;

        host.Dispose();

        Log.Logger.ShouldBeSameAs(replacement);
        Log.Logger = Logger.None;
    }

    [Fact]
    public void CreateLogger_ShouldWriteTheConsoleLineAsBefore_WhenTheHostIsTheOnlyOne()
    {
        // The Serilog section's console sink and template, as a service configures them: the same line as before.
        const string template = "[{Level:u3}] {SourceContext}: {Message:lj} ({Writer}){NewLine}";
        using var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        try
        {
            using var host = TestHost.Build(
                "only",
                new Dictionary<string, string?>
                {
                    ["Serilog:Using:0"] = "Serilog.Sinks.Console",
                    ["Serilog:WriteTo:0:Name"] = "Console",
                    ["Serilog:WriteTo:0:Args:outputTemplate"] = template,
                    ["Serilog:WriteTo:0:Args:theme"] = "Serilog.Sinks.SystemConsole.Themes.ConsoleTheme::None, Serilog.Sinks.Console",
                });

            var logger = host.CreateLogger();
            LogHello(logger, "only");
            Log.ForContext(WriterProperty, "static").Information("Hello from the static logger");
        }
        finally
        {
            Console.SetOut(original);
        }

        var lines = console.ToString().Split(Environment.NewLine);
        lines.ShouldContain("[INF] test: Hello from only (only)");
        lines.ShouldContain("[INF] : Hello from the static logger (static)");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Hello from {Writer}")]
    private static partial void LogHello(ILogger logger, string writer);

    /// <summary>A host's container with <c>AddServiceTelemetry</c>'s logging, writing to a sink of its own.</summary>
    private sealed class TestHost : IDisposable
    {
        private readonly string _name;
        private readonly ServiceProvider _provider;

        private TestHost(
            string name,
            ServiceProvider provider,
            DisposableSink sink)
        {
            _name = name;
            _provider = provider;
            Sink = sink;
            // Resolved as a host resolves it while it is built.
            Factory = (HostLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        }

        public DisposableSink Sink { get; }

        public HostLoggerFactory Factory { get; }

        public static TestHost Build(
            string name,
            Dictionary<string, string?>? settings = null)
        {
            var sink = new DisposableSink();
            var services = new ServiceCollection();
            services.AddSingleton<ILogEventSink>(sink);
            services.AddServiceTelemetry(
                Telemetry.ServiceName,
                Telemetry.Configuration(settings),
                options =>
                {
                    options.Tracing = false;
                    options.Metrics = false;
                    options.ConfigureLogging = logging => logging.Enrich.WithProperty(HostProperty, name);
                });
            return new TestHost(name, services.BuildServiceProvider(), sink);
        }

        public ILogger CreateLogger()
            => Factory.CreateLogger("test");

        /// <summary>Who wrote each event this host's logger took (its <c>Writer</c>), checking each is this host's.</summary>
        public IReadOnlyList<string?> Writers()
        {
            // Every line this host's logger wrote carries this host's name (the Serilog section's properties, as a
            // service's): none is another host's.
            Sink.Events.Select(logged => Scalar(logged, HostProperty)).ShouldAllBe(host => host == _name);
            return [.. Sink.Events.Select(logged => Scalar(logged, WriterProperty))];
        }

        public void Dispose()
            => _provider.Dispose();

        private static string? Scalar(
            LogEvent logged,
            string property)
            => logged.Properties.TryGetValue(property, out var value) && value is ScalarValue { Value: string text }
                ? text
                : null;
    }

    /// <summary>Keeps every event, and knows when its logger closed it.</summary>
    private sealed class DisposableSink
        : ILogEventSink,
        IDisposable
    {
        private int _disposed;

        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public void Emit(LogEvent logEvent)
        {
            if (!IsDisposed)
                Events.Enqueue(logEvent);
        }

        public void Dispose()
            => Volatile.Write(ref _disposed, 1);
    }
}
