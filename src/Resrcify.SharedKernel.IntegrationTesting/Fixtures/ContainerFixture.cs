using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Resrcify.SharedKernel.IntegrationTesting.Fixtures;

/// <summary>
/// xUnit fixture base that owns the lifecycle of a single Testcontainers
/// container. Subclasses produce the concrete container via <see cref="Build"/>;
/// this base only starts and disposes it.
/// </summary>
public abstract class ContainerFixture<TContainer>
    : IAsyncLifetime
    where TContainer : IContainer
{
    public TContainer Container { get; private set; } = default!;

    protected abstract TContainer Build();

    public async Task InitializeAsync()
    {
        Container = Build();
        await Container.StartAsync();
    }

    public async Task DisposeAsync()
        => await Container.DisposeAsync();
}
