using System;
using System.Threading;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentValidation;
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

        services.AddMediator(config => config.RegisterServicesFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly));

        using var serviceProvider = services.BuildServiceProvider();

        var sender = serviceProvider.GetService<ISender>();
        sender.ShouldNotBeNull();

        serviceProvider.GetService<IPublisher>().ShouldNotBeNull();
        serviceProvider.GetService<IMediator>().ShouldNotBeNull();

        var response = await sender.Send(new PingRequest(), CancellationToken.None);
        response.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void AddMediator_WithAScopedLifetime_RegistersEveryInterfaceAsTheScopesOneMediator()
    {
        var services = new ServiceCollection();

        services.AddMediator(config => config
            .RegisterServicesFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly)
            .UseMediatorLifetime(ServiceLifetime.Scoped));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var first = scope.ServiceProvider.GetRequiredService<IMediator>();
        var second = scope.ServiceProvider.GetRequiredService<IMediator>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        first.ShouldBeSameAs(second);
        sender.ShouldBeSameAs(first);
    }

    [Fact]
    public void AddMediator_ShouldRegisterATransientMediatorAndContainerComposedPipelines_ByDefault()
    {
        var services = new ServiceCollection();

        services.AddMediator(config => config.RegisterServicesFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly));

        services.Single(descriptor => descriptor.ServiceType == typeof(IMediator)).Lifetime.ShouldBe(ServiceLifetime.Transient);
        services.Single(descriptor => descriptor.ServiceType == typeof(ISender)).Lifetime.ShouldBe(ServiceLifetime.Transient);
        services
            .Single(descriptor => descriptor.ServiceType == typeof(Abstractions.IDiComposedSendRuntime<,>))
            .Lifetime.ShouldBe(ServiceLifetime.Transient);
        services
            .Single(descriptor => descriptor.ServiceType == typeof(Abstractions.IDiComposedStreamRuntime<,>))
            .Lifetime.ShouldBe(ServiceLifetime.Transient);
    }

    [Fact]
    public async Task AddMediator_ShouldRegisterTheScannedValidators_InternalOnesIncluded()
    {
        var services = new ServiceCollection();

        services.AddMediator(config => config.RegisterServicesFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider
            .GetServices<IValidator<ValidatedRequest>>()
            .ShouldHaveSingleItem()
            .ShouldBeOfType<ValidatedRequestValidator>();
    }

    [Fact]
    public async Task Send_ShouldFailWithTheScannedValidatorsErrors_WhenTheValidationBehaviorIsAdded()
    {
        var services = new ServiceCollection();
        services.AddMediator(config => config
            .RegisterServicesFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly)
            .AddOpenBehavior(typeof(ValidationPipelineBehavior<,>)));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ValidatedRequest(string.Empty));

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(nameof(ValidatedRequest.Name));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddMediator_ShouldRegisterAValidatorOnce_WhenTheServiceAlsoCallsAddValidatorsFromAssembly(bool before)
    {
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;
        var services = new ServiceCollection();

        if (before)
            services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddMediator(config => config.RegisterServicesFromAssemblies(assembly));
        if (!before)
            services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.Count(descriptor => descriptor.ServiceType == typeof(IValidator<ValidatedRequest>)).ShouldBe(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(ValidatedRequestValidator)).ShouldBe(1);
    }

    [Fact]
    public void AddMediator_ShouldRegisterAValidatorOnce_WhenTwoCallsScanTheSameAssembly()
    {
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;
        var services = new ServiceCollection();

        services.AddMediator(config => config.RegisterServicesFromAssemblies(assembly));
        services.AddMediator(config => config.RegisterServicesFromAssemblies(assembly));

        services.Count(descriptor => descriptor.ServiceType == typeof(IValidator<ValidatedRequest>)).ShouldBe(1);
    }

    [Fact]
    public void AddMediator_ShouldRegisterNoValidator_WhenToldToSkipTheScan()
    {
        var services = new ServiceCollection();

        services.AddMediator(config => config
            .RegisterServicesFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly)
            .SkipValidatorScan());

        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IValidator<ValidatedRequest>));
    }

    [Fact]
    public void AddMediator_ShouldThrow_WhenTheMediatorLifetimeIsSingleton()
    {
        var services = new ServiceCollection();

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
        services.AddMediator(config => config.RegisterServicesFromAssemblies(typeof(ServiceCollectionExtensionsTests).Assembly));

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
                typeof(NonConcurrentPipelineBehavior<,>),
                typeof(TransactionPipelineBehavior<,>),
                typeof(UnitOfWorkPipelineBehavior<,>),
            ]);
    }

    // The documented order (StandardBehavior, AddStandardBehaviors, the README): idempotency right after logging,
    // non-concurrent right after validation, whatever order the enum declares its members in.
    [Fact]
    public void AddMediator_ShouldRegisterTheDocumentedOrder_WhenTheStandardBehaviorsAreAddedUnchanged()
    {
        var services = new ServiceCollection();

        services.AddMediator(config => config.AddStandardBehaviors());

        services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .ShouldBe(
            [
                typeof(LoggingPipelineBehavior<,>),
                typeof(IdempotencyPipelineBehavior<,>),
                typeof(ValidationPipelineBehavior<,>),
                typeof(NonConcurrentPipelineBehavior<,>),
                typeof(TransactionPipelineBehavior<,>),
                typeof(UnitOfWorkPipelineBehavior<,>),
                typeof(CachingPipelineBehavior<,>),
            ]);
    }

    [Fact]
    public void AddMediator_ShouldPutABehaviorInsertedBeforeValidationAfterIdempotency_WhenAskedFor()
    {
        var services = new ServiceCollection();

        services.AddMediator(config => config
            .AddStandardBehaviors(standard => standard.InsertBefore(StandardBehavior.Validation, typeof(ScopedOpenBehavior<,>))));

        services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .Take(4)
            .ShouldBe(
            [
                typeof(LoggingPipelineBehavior<,>),
                typeof(IdempotencyPipelineBehavior<,>),
                typeof(ScopedOpenBehavior<,>),
                typeof(ValidationPipelineBehavior<,>),
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

    private sealed record ValidatedRequest(string Name) : IRequest<Result>;

    private sealed class ValidatedRequestHandler : IRequestHandler<ValidatedRequest, Result>
    {
        public Task<Result> Handle(ValidatedRequest request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    // Internal (nested, private), as Tournament's validators are: AddValidatorsFromAssembly's default skips them.
    private sealed class ValidatedRequestValidator : AbstractValidator<ValidatedRequest>
    {
        public ValidatedRequestValidator()
            => RuleFor(request => request.Name).NotEmpty();
    }

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