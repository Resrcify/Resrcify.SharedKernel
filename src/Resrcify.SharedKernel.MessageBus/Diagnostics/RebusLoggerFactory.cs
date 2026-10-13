using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Rebus.Logging;

namespace Resrcify.SharedKernel.MessageBus.Diagnostics;

/// <summary>Routes Rebus' own logging into the service's <see cref="ILoggerFactory"/>.</summary>
internal sealed class RebusLoggerFactory(ILoggerFactory loggerFactory)
    : IRebusLoggerFactory
{
    public ILog GetLogger<T>()
        => GetLogger(typeof(T));

    public ILog GetLogger(Type type)
        => new Log(loggerFactory.CreateLogger(type));

    [SuppressMessage(
        "Usage",
        "CA2254:Template should be a static expression",
        Justification = "Forwards Rebus' own message templates, which use the same {Name} placeholder syntax.")]
    [SuppressMessage(
        "Performance",
        "CA1848:Use the LoggerMessage delegates",
        Justification = "An adapter for templates only known at runtime.")]
    private sealed class Log(ILogger logger) : ILog
    {
        public void Debug(string message, params object[] objs) => logger.LogDebug(message, objs);
        public void Info(string message, params object[] objs) => logger.LogInformation(message, objs);
        public void Warn(string message, params object[] objs) => logger.LogWarning(message, objs);
        public void Warn(Exception exception, string message, params object[] objs) => logger.LogWarning(exception, message, objs);
        public void Error(string message, params object[] objs) => logger.LogError(message, objs);
        public void Error(Exception exception, string message, params object[] objs) => logger.LogError(exception, message, objs);
    }
}
