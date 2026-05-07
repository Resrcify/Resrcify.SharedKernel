using System.Diagnostics.CodeAnalysis;
using Resrcify.SharedKernel.ArchitectureTesting.Tests;

namespace Resrcify.SharedKernel.WebApiExample.ArchitectureTests.Tests;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes for discovery.")]
public sealed class ApplicationTests : ConventionalApplicationTests;
