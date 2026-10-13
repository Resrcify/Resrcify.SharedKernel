using System;

namespace Resrcify.SharedKernel.IntegrationTesting.Fixtures;

/// <summary>The container images the environment asks for in place of a fixture's own.</summary>
internal static class ContainerImages
{
    /// <summary>The image named by <paramref name="variable"/>, or null when it isn't set.</summary>
    public static string? FromEnvironment(string variable)
        => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } image
            ? image.Trim()
            : null;
}
