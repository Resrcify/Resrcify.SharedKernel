using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.IntegrationTesting.Factories;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Fixtures;

/// <summary>A service with a database only, defaults everywhere.</summary>
[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit class fixtures must be public")]
public sealed class DatabaseOnlyServiceHostFixture
    : ServiceHostFixture<Program>;
