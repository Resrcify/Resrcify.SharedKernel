using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Resrcify.SharedKernel.IntegrationTesting.TestService;

/// <summary>The configuration as <c>Program</c> saw it while registering its services.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "Read by the test project")]
public sealed record RegistrationSettings(
    IReadOnlyDictionary<string, string?> Values);
