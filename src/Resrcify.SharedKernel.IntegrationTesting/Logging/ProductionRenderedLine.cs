using Serilog.Events;

namespace Resrcify.SharedKernel.IntegrationTesting.Logging;

/// <summary>A line as production prints it (<see cref="ProductionRenderedLogs"/>): its level, template and rendered message.</summary>
public sealed record ProductionRenderedLine(
    LogEventLevel Level,
    string Template,
    string Message);
