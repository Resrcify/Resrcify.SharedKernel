namespace Resrcify.SharedKernel.MessageBus.IntegrationTests.Support;

internal sealed record PingRequest(string Value);

internal sealed record PingResponse(string Value);

internal static class WireNames
{
    public const string PingRequest = "test.ping.request.v1";
    public const string PingResponse = "test.ping.response.v1";
}
