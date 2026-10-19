using System;
using System.Net;
using System.Threading.Tasks;
using Docker.DotNet;
using DotNet.Testcontainers.Configurations;

namespace Resrcify.SharedKernel.IntegrationTesting.IntegrationTests.Docker;

/// <summary>Asks Docker itself (not Testcontainers' view of it) whether a container or a network still exists.</summary>
internal static class DockerResources
{
    public static Task<bool> ContainerExistsAsync(string id)
        => ExistsAsync(client => client.Containers.InspectContainerAsync(id));

    public static Task<bool> NetworkExistsAsync(string name)
        => ExistsAsync(client => client.Networks.InspectNetworkAsync(name));

    private static async Task<bool> ExistsAsync(Func<DockerClient, Task> inspect)
    {
        using var client = TestcontainersSettings.OS.DockerEndpointAuthConfig
            .GetDockerClientBuilder(Guid.NewGuid())
            .Build();
        try
        {
            await inspect(client);
            return true;
        }
        catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
