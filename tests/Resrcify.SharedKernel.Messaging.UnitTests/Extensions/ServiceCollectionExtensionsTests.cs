using System.Threading;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Messaging;
using Resrcify.SharedKernel.Messaging.Extensions;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Messaging.UnitTests.Extensions;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddMediator_RegistersAndUsesCustomMediatorAbstractions()
    {
        var services = new ServiceCollection();

        services.AddMediator(typeof(ServiceCollectionExtensionsTests).Assembly);

        using var serviceProvider = services.BuildServiceProvider();

        var sender = serviceProvider.GetService<ISender>();
        sender.ShouldNotBeNull();

        serviceProvider.GetService<IPublisher>().ShouldNotBeNull();
        serviceProvider.GetService<IMediator>().ShouldNotBeNull();

        var response = await sender.Send(new PingRequest(), CancellationToken.None);
        response.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AddMediator_WithServiceLifetimeOverload_RegistersMediatorAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddMediator(ServiceLifetime.Singleton, typeof(ServiceCollectionExtensionsTests).Assembly);

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IMediator>();
        var second = provider.GetRequiredService<IMediator>();
        var sender = provider.GetRequiredService<ISender>();

        first.ShouldBeSameAs(second);
        sender.ShouldBeSameAs(first);
    }

    [Fact]
    public void AddMediator_WithConfiguration_UsesConfiguredScopedLifetime()
    {
        var services = new ServiceCollection();

        services.AddMediator(config =>
        {
            config.RegisterServicesFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly);
            config.UseMediatorLifetime(ServiceLifetime.Scoped);
        });

        using var rootProvider = services.BuildServiceProvider();
        using var firstScope = rootProvider.CreateScope();
        using var secondScope = rootProvider.CreateScope();

        var firstInScope = firstScope.ServiceProvider.GetRequiredService<IMediator>();
        var secondInSameScope = firstScope.ServiceProvider.GetRequiredService<IMediator>();
        var inOtherScope = secondScope.ServiceProvider.GetRequiredService<IMediator>();

        firstInScope.ShouldBeSameAs(secondInSameScope);
        inOtherScope.ShouldNotBeSameAs(firstInScope);
    }

    [Fact]
    public void AddMediator_WithOpenBehaviorLifetime_RegistersConfiguredBehaviorAsScoped()
    {
        var services = new ServiceCollection();

        services.AddMediator(config =>
            config.AddOpenBehavior(typeof(ScopedOpenBehavior<,>), ServiceLifetime.Scoped));

        var behaviorDescriptors = services
            .Where(descriptor => descriptor.ImplementationType == typeof(ScopedOpenBehavior<,>))
            .ToArray();

        behaviorDescriptors.ShouldNotBeEmpty();
        behaviorDescriptors.Length.ShouldBe(1);
        behaviorDescriptors[0].ServiceType.ShouldBe(typeof(IPipelineBehavior<,>));
        behaviorDescriptors[0].Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddMediator_WithAssemblyScanAndOpenBehaviorLifetime_OverridesScannedTransientLifetime()
    {
        var services = new ServiceCollection();

        services.AddMediator(config =>
        {
            config.RegisterServicesFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly);
            config.AddOpenBehavior(typeof(ScopedOpenBehavior<,>), ServiceLifetime.Scoped);
        });

        var behaviorDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>) &&
                                 descriptor.ImplementationType == typeof(ScopedOpenBehavior<,>))
            .ToArray();

        behaviorDescriptors.Length.ShouldBe(1);
        behaviorDescriptors[0].Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    private sealed class PingRequest : IRequest<Result>;

    private sealed class ScopedOpenBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class PingRequestHandler : IRequestHandler<PingRequest, Result>
    {
        public Task<Result> Handle(PingRequest request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }
}