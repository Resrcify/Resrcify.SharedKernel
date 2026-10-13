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

    /// <summary>
    /// Runs after <c>StartAsync</c>. Override to verify the container is genuinely
    /// ready to serve before tests run, closing cold-start readiness races.
    /// </summary>
    protected virtual Task OnStartedAsync()
        => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        Container = Build();
        await Container.StartAsync();
        await OnStartedAsync();
    }

    /// <summary>Disposes the container; nothing when it was never built (<see cref="Build"/> threw).</summary>
    public async Task DisposeAsync()
    {
        if (Container is not null)
            await Container.DisposeAsync();
    }
}
