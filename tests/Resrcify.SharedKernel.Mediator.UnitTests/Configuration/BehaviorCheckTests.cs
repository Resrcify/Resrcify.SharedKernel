using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Mediator.Configuration;
using Resrcify.SharedKernel.Results.Primitives;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Configuration;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class BehaviorCheckTests
{
    [Fact]
    public void RunFor_ShouldThrow_WhenACachingQueryHasNoCachingBehavior()
    {
        var failure = Should.Throw<InvalidOperationException>(() => BehaviorCheck.RunFor(
            new ServiceCollection(),
            [Task(typeof(CachedQuery)), Task(typeof(PlainQuery))],
            [typeof(LoggingPipelineBehavior<,>)]));

        failure.Message.ShouldStartWith("1 request type(s) implement ICachingQuery (CachedQuery)");
        failure.Message.ShouldContain("CachingPipelineBehavior");
    }

    [Fact]
    public void RunFor_ShouldThrow_WhenATransactionalCommandHasNoTransactionBehavior()
        => Should.Throw<InvalidOperationException>(() => BehaviorCheck.RunFor(
                new ServiceCollection(),
                [Task(typeof(TransactionalCommand))],
                [typeof(CachingPipelineBehavior<,>)]))
            .Message.ShouldContain("TransactionPipelineBehavior");

    [Fact]
    public void RunFor_ShouldPass_WhenTheStandardBehaviorsAreConfigured()
    {
        var configuration = new MediatorConfiguration().AddStandardBehaviors();

        Should.NotThrow(() => BehaviorCheck.RunFor(
            new ServiceCollection(),
            [Task(typeof(CachedQuery)), Task(typeof(TransactionalCommand))],
            configuration.OpenBehaviorTypes));
    }

    [Fact]
    public void RunFor_ShouldPass_WhenTheServicesOwnBehaviorIsConstrainedToTheInterface()
        => Should.NotThrow(() => BehaviorCheck.RunFor(
            new ServiceCollection(),
            [Task(typeof(CachedQuery))],
            [typeof(OwnCachingBehavior<,>)]));

    [Fact]
    public void RunFor_ShouldPass_WhenTheBehaviorWasRegisteredInTheContainerBefore()
    {
        var services = new ServiceCollection();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingPipelineBehavior<,>));

        Should.NotThrow(() => BehaviorCheck.RunFor(services, [Task(typeof(CachedQuery))], []));
    }

    [Fact]
    public void RunFor_ShouldPass_WhenNoRequestAsksForABehavior()
        => Should.NotThrow(() => BehaviorCheck.RunFor(
            new ServiceCollection(),
            [Task(typeof(PlainQuery))],
            []));

    [Fact]
    public void RunFor_ShouldThrow_WhenAValueTaskHandledCachingQueryHasOnlyTaskBehaviors()
    {
        var configuration = new MediatorConfiguration().AddStandardBehaviors();

        var failure = Should.Throw<InvalidOperationException>(() => BehaviorCheck.RunFor(
            new ServiceCollection(),
            [ValueTask(typeof(CachedQuery))],
            configuration.OpenBehaviorTypes));

        failure.Message.ShouldContain("ValueTask handler");
    }

    [Fact]
    public void RunFor_ShouldThrow_WhenATaskHandledCachingQueryHasOnlyAValueTaskBehavior()
        => Should.Throw<InvalidOperationException>(() => BehaviorCheck.RunFor(
            new ServiceCollection(),
            [Task(typeof(CachedQuery))],
            [typeof(OwnValueTaskCachingBehavior<,>)]));

    [Fact]
    public void RunFor_ShouldPass_WhenAValueTaskBehaviorHandlesAValueTaskHandledRequest()
        => Should.NotThrow(() => BehaviorCheck.RunFor(
            new ServiceCollection(),
            [ValueTask(typeof(CachedQuery))],
            [typeof(OwnValueTaskCachingBehavior<,>)]));

    [Fact]
    public void RunFor_ShouldSayToRegisterBeforeAddMediator_ForTheAssembliesOverload()
        => Should.Throw<InvalidOperationException>(() => BehaviorCheck.RunFor(
                new ServiceCollection(),
                [Task(typeof(CachedQuery))],
                [],
                BehaviorCheck.AssembliesRemedy))
            .Message.ShouldContain("before AddMediator(assemblies)");

    [Fact]
    public void HandledRequests_ShouldBeTheRequestsWithAHandler_WhenAnAssemblyIsScanned()
    {
        var requests = BehaviorCheck.HandledRequests([typeof(BehaviorCheckTests).Assembly]);

        requests.ShouldContain(request => request.Request.Name == "ComposedRequest");   // has a handler
        requests.ShouldNotContain(request => request.Request == typeof(CachedQuery));   // has none
    }

    private static (Type Request, BehaviorCheck.PipelineKind Kind) Task(Type request)
        => (request, BehaviorCheck.PipelineKind.Task);

    private static (Type Request, BehaviorCheck.PipelineKind Kind) ValueTask(Type request)
        => (request, BehaviorCheck.PipelineKind.ValueTask);

    private sealed class CachedQuery : ICachingQuery<int>
    {
        public string? CacheKey { get; set; } = "cached";

        public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(1);
    }

    private sealed class PlainQuery : IQuery<int>;

    private sealed class TransactionalCommand : ITransactionCommand
    {
        public TimeSpan? CommandTimeout => null;

        public System.Data.IsolationLevel? IsolationLevel => null;
    }

    private sealed class OwnValueTaskCachingBehavior<TRequest, TResponse>
        : IValueTaskPipelineBehavior<TRequest, TResponse>
        where TRequest : ICachingQuery
        where TResponse : Result
    {
        public ValueTask<TResponse> Handle(
            TRequest request,
            ValueTaskRequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class OwnCachingBehavior<TRequest, TResponse>
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : ICachingQuery
        where TResponse : Result
    {
        public Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
            => next(cancellationToken);
    }
}
