namespace Resrcify.SharedKernel.IntegrationTesting.TestService;

internal sealed class Greeter
    : IGreeter
{
    public string Greet()
        => "Hello from the service";
}
