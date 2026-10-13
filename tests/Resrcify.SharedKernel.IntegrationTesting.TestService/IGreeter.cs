using System.Diagnostics.CodeAnalysis;

namespace Resrcify.SharedKernel.IntegrationTesting.TestService;

/// <summary>A service of the service's own, for the tests to replace.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "Replaced by the test project")]
public interface IGreeter
{
    string Greet();
}
