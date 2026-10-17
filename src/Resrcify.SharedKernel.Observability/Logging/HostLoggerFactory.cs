using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;

namespace Resrcify.SharedKernel.Observability.Logging;

/// <summary>
/// A host's logger factory, as <c>AddServiceTelemetry</c> registers it: Serilog's, on the host's own logger
/// (<c>AddSerilog(..., preserveStaticLogger: true)</c>), so every <see cref="Microsoft.Extensions.Logging.ILogger"/> the host
/// makes writes to that logger, whenever it is made, and the host's stop disposes that logger and nothing else.
/// </summary>
/// <remarks>
/// Serilog's default (<c>preserveStaticLogger: false</c>) made the host's loggers write through the static
/// <see cref="Log.Logger"/> of the moment each was made, and closed the static logger when the host stopped
/// (<c>Log.CloseAndFlush</c>): with two hosts in one process (a test's), a logger the first one made after the second
/// started wrote as the second, and the first one's stop closed the second's logger.
/// <para>
/// For code logging through Serilog's static API, the host's logger is still <see cref="Log.Logger"/>, as before, while
/// no other host's is: the first host in the process (the only one, in a service) sets it when its logger factory is
/// made (as the host is built) and, when it stops, sets it back to <see cref="Logger.None"/> (as
/// <c>Log.CloseAndFlush</c> did) unless something else has replaced it meanwhile. A host made after that takes it then.
/// </para>
/// </remarks>
internal sealed class HostLoggerFactory
    : ILoggerFactory
{
    private static readonly Lock StaticLoggerGate = new();
    private static HostLoggerFactory? _staticLoggerOwner;

    private readonly ILoggerFactory _serilog;
    private readonly Serilog.ILogger _hostLogger;

    private HostLoggerFactory(
        ILoggerFactory serilog,
        Serilog.ILogger hostLogger)
    {
        _serilog = serilog;
        _hostLogger = hostLogger;
        ClaimStaticLogger(this);
    }

    /// <summary>Whether this host's logger is the static <see cref="Log.Logger"/>'s owner. For tests.</summary>
    internal bool OwnsStaticLogger
    {
        get
        {
            lock (StaticLoggerGate)
                return ReferenceEquals(_staticLoggerOwner, this);
        }
    }

    /// <summary>
    /// Wraps the logger factory <c>AddSerilog</c> registered (among the descriptors from <paramref name="firstAdded"/> on).
    /// </summary>
    /// <exception cref="InvalidOperationException">Serilog registered no logger factory there (a Serilog that works differently).</exception>
    public static void Decorate(
        IServiceCollection services,
        int firstAdded)
    {
        for (var index = services.Count - 1; index >= firstAdded; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType != typeof(ILoggerFactory)
                || descriptor.IsKeyedService
                || descriptor.ImplementationFactory is not { } serilogFactory)
                continue;

            services[index] = ServiceDescriptor.Singleton<ILoggerFactory>(provider => new HostLoggerFactory(
                (ILoggerFactory)serilogFactory(provider),
                provider.GetRequiredService<Serilog.ILogger>()));
            return;
        }

        throw new InvalidOperationException(
            "AddServiceTelemetry wraps the logger factory Serilog's AddSerilog registers, but it registered none: " +
            "this Serilog.Extensions.Hosting works differently from the one AddServiceTelemetry was built against.");
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
        => _serilog.CreateLogger(categoryName);

    public void AddProvider(ILoggerProvider provider)
        => _serilog.AddProvider(provider);

    public void Dispose()
    {
        ReleaseStaticLogger(this);
        _serilog.Dispose();
    }

    private static void ClaimStaticLogger(HostLoggerFactory host)
    {
        lock (StaticLoggerGate)
        {
            if (_staticLoggerOwner is not null)
                return;
            _staticLoggerOwner = host;
            Log.Logger = host._hostLogger;
        }
    }

    private static void ReleaseStaticLogger(HostLoggerFactory host)
    {
        lock (StaticLoggerGate)
        {
            if (!ReferenceEquals(_staticLoggerOwner, host))
                return;
            _staticLoggerOwner = null;
            if (ReferenceEquals(Log.Logger, host._hostLogger))
                Log.Logger = Logger.None;
        }
    }
}
