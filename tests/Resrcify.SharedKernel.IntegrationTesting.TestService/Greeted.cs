using System.Diagnostics.CodeAnalysis;

namespace Resrcify.SharedKernel.IntegrationTesting.TestService;

/// <summary>The integration event the service publishes on <c>POST /greeted/{name}</c>.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "The tests subscribe to it, as another service would")]
public sealed record Greeted(string Name);
