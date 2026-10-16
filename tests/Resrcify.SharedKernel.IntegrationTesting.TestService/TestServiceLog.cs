using Microsoft.Extensions.Logging;

namespace Resrcify.SharedKernel.IntegrationTesting.TestService;

/// <summary>What the service logs on <c>POST /log/{message}</c>.</summary>
internal static partial class TestServiceLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Logged {Message}")]
    public static partial void Logged(ILogger logger, string message);
}
