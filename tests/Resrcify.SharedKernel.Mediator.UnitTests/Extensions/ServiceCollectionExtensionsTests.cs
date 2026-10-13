using System;
using System.Threading;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Mediator.Configuration;
using Resrcify.SharedKernel.Mediator.Extensions;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Extensions;

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
    public void AddMediator_WithServiceLifetimeOverload_RegistersMediatorAsScoped()
    {
        var services = new ServiceCollection();

        services.AddMediator(ServiceLifetime.Scoped, typeof(ServiceCollectionExtensionsTests).Assembly);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var first = scope.ServiceProvider.GetRequiredService<IMediator>();
        var second = scope.ServiceProvider.GetRequiredService<IMediator>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        first.ShouldBeSameAs(second);
        sender.ShouldBeSameAs(first);
    }

    [Fact]
    public void AddMediator_ShouldThrow_WhenTheMediatorLifetimeIsSingleton()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentOutOfRangeException>(
            () => services.AddMediator(ServiceLifetime.Singleton, typeof(ServiceCollectionExtensionsTests).Assembly));
        Should.Throw<ArgumentOutOfRangeException>(
            () => services.AddMediator(config => config.UseMediatorLifetime(ServiceLifetime.Singleton)));
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
    public void AddMediator_WithAssemblyScanAndOpenBehaviorLifetime_RegistersTheBehaviorOnceWithItsLifetime()
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

    [Fact]
    public void AddMediator_ShouldRecordTheScannedAssemblies_WhenItScans()
    {
        var services = new ServiceCollection();
        services.AddMediator(typeof(ServiceCollectionExtensionsTests).Assembly);

        services
            .Where(descriptor => descriptor.ServiceType == typeof(IMediatorAssemblyScan))
            .Select(descriptor => ((IMediatorAssemblyScan)descriptor.ImplementationInstance!).Assemblies)
            .ShouldHaveSingleItem()
            .ShouldBe([typeof(ServiceCollectionExtensionsTests).Assembly]);
    }

    [Fact]
    public void AddMediator_ShouldTellRegisteredListeners_WhenItScans()
    {
        var listener = new RecordingScanListener();
        var services = new ServiceCollection();
        services.AddSingleton<IMediatorAssemblyScanListener>(listener);

        services.AddMediator(config => config.RegisterServicesFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly));

        listener.Scans.ShouldHaveSingleItem().ShouldBe([typeof(ServiceCollectionExtensionsTests).Assembly]);
    }

    [Fact]
    public void AddMediator_ShouldRegisterHandlersOnly_WhenItScansAnAssemblyWithBehaviorsAndProcessors()
    {
        var services = new ServiceCollection();

        services.AddMediator(config => config.RegisterServicesFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly));

        services.ShouldContain(descriptor => descriptor.ImplementationType == typeof(PingRequestHandler));
        services.ShouldNotContain(descriptor => descriptor.ImplementationType == typeof(ScopedOpenBehavior<,>));
        services.ShouldNotContain(descriptor => descriptor.ImplementationType == typeof(PingPreProcessor));
        services.ShouldNotContain(descriptor => descriptor.ImplementationType == typeof(OpenPostProcessor<,>));
    }

    [Fact]
    public void AddMediator_ShouldRegisterTheStandardBehaviorsInOrder_WhenAskedFor()
    {
        var services = new ServiceCollection();

        services.AddMediator(config => config
            .AddStandardBehaviors(standard => standard
                .InsertAfter(StandardBehavior.Validation, typeof(ScopedOpenBehavior<,>))
                .Without(StandardBehavior.Caching)));

        services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .ShouldBe(
            [
                typeof(LoggingPipelineBehavior<,>),
                typeof(IdempotencyPipelineBehavior<,>),
                typeof(ValidationPipelineBehavior<,>),
                typeof(ScopedOpenBehavior<,>),
                typeof(TransactionPipelineBehavior<,>),
                typeof(UnitOfWorkPipelineBehavior<,>),
            ]);
    }

    [Fact]
    public async Task AddMediator_ShouldRunTheProcessors_WhenTheyAreAddedOpenOrClosed()
    {
        var services = new ServiceCollection();
        var trace = new ProcessorTrace();
        services.AddSingleton(trace);

        services.AddMediator(config => config
            .RegisterServicesFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly)
            .AddRequestPreProcessor(typeof(PingPreProcessor))
            .AddRequestPostProcessor(typeof(OpenPostProcessor<,>)));

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ISender>().Send(new PingRequest(), CancellationToken.None);

        trace.Steps.ShouldBe(["Pre:PingRequest", "Post:PingRequest"]);
    }

    [Fact]
    public void AddRequestPreProcessor_ShouldThrow_WhenTheTypeIsNotAPreProcessor()
        => Should.Throw<ArgumentException>(
            () => new MediatorConfiguration().AddRequestPreProcessor(typeof(PingRequestHandler)));

    [Fact]
    public void AddOpenBehavior_ShouldRegisterAStreamBehavior_WhenGivenOne()
    {
        var services = new ServiceCollection();

        services.AddMediator(config => config.AddOpenBehavior(typeof(OpenStreamBehavior<,>)));

        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IStreamPipelineBehavior<,>)
            && descriptor.ImplementationType == typeof(OpenStreamBehavior<,>));
    }

    [Fact]
    public void AddMediator_ShouldRegisterTheLoggingOptions_WhenLoggingIsConfigured()
    {
        var services = new ServiceCollection();

        services.AddMediator(config => config.ConfigureLogging(logging =>
        {
            logging.RequestLevel = Microsoft.Extensions.Logging.LogLevel.Debug;
            logging.SlowRequestThreshold = TimeSpan.FromSeconds(2);
        }));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<LoggingPipelineOptions>();
        options.RequestLevel.ShouldBe(Microsoft.Extensions.Logging.LogLevel.Debug);
        options.SlowRequestThreshold.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void AddMediator_ShouldRegisterTheUnitOfWorkOptions_WhenTheUnitOfWorkIsConfigured()
    {
        var services = new ServiceCollection();

        services.AddMediator(config => config.ConfigureUnitOfWork(unitOfWork => unitOfWork.ReturnPersistenceFailures = true));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<UnitOfWorkPipelineOptions>().ReturnPersistenceFailures.ShouldBeTrue();
    }

    [Fact]
    public void AddMediator_ShouldKeepTheConfiguredOptions_WhenAnotherCallLeavesThemDefault()
    {
        var services = new ServiceCollection();

        services.AddMediator(_ => { });
        services.AddMediator(config => config
            .ConfigureUnitOfWork(unitOfWork => unitOfWork.ReturnPersistenceFailures = true)
            .ConfigureLogging(logging => logging.RequestLevel = Microsoft.Extensions.Logging.LogLevel.Debug));
        services.AddMediator(_ => { });

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<UnitOfWorkPipelineOptions>().ReturnPersistenceFailures.ShouldBeTrue();
        provider.GetRequiredService<LoggingPipelineOptions>().RequestLevel.ShouldBe(Microsoft.Extensions.Logging.LogLevel.Debug);
        services.Count(descriptor => descriptor.ServiceType == typeof(UnitOfWorkPipelineOptions)).ShouldBe(1);
    }

    [Fact]
    public void AddMediator_ShouldThrow_WhenTwoCallsConfigureTheSameOptions()
    {
        var services = new ServiceCollection();
        services.AddMediator(config => config.ConfigureUnitOfWork(unitOfWork => unitOfWork.ReturnPersistenceFailures = true));

        Should.Throw<InvalidOperationException>(() => services.AddMediator(
                config => config.ConfigureUnitOfWork(unitOfWork => unitOfWork.ReturnPersistenceFailures = false)))
            .Message.ShouldContain("ConfigureUnitOfWork");
    }

    [Fact]
    public void AddMediator_ShouldRegisterUnitOfWorkOptionsThatThrow_ByDefault()
    {
        var services = new ServiceCollection();

        services.AddMediator(_ => { });

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<UnitOfWorkPipelineOptions>().ReturnPersistenceFailures.ShouldBeFalse();
    }

    private sealed class ProcessorTrace
    {
        public List<string> Steps { get; } = [];
    }

    private sealed class PingPreProcessor(ProcessorTrace trace) : IRequestPreProcessor<PingRequest>
    {
        public Task Process(PingRequest request, CancellationToken cancellationToken)
        {
            trace.Steps.Add("Pre:PingRequest");
            return Task.CompletedTask;
        }
    }

    private sealed class OpenPostProcessor<TRequest, TResponse>(ProcessorTrace trace) : IRequestPostProcessor<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task Process(TRequest request, TResponse response, CancellationToken cancellationToken)
        {
            trace.Steps.Add($"Post:{typeof(TRequest).Name}");
            return Task.CompletedTask;
        }
    }

    private sealed class OpenStreamBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
        where TRequest : IStreamRequest<TResponse>
    {
        public IAsyncEnumerable<TResponse> Handle(
            TRequest request,
            StreamHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class RecordingScanListener : IMediatorAssemblyScanListener
    {
        public List<IReadOnlyCollection<Assembly>> Scans { get; } = [];

        public void OnScanned(IServiceCollection services, IReadOnlyCollection<Assembly> assemblies)
            => Scans.Add(assemblies);
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