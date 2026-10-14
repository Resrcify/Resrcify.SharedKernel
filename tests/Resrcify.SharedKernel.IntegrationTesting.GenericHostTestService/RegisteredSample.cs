using System.Diagnostics.CodeAnalysis;

namespace Resrcify.SharedKernel.IntegrationTesting.GenericHostTestService;

/// <summary><c>Sample:Value</c> as <see cref="Startup"/> saw it while registering its services.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "Read by the test project")]
public sealed record RegisteredSample(
    string? Value);
