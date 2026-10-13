using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resrcify.SharedKernel.Abstractions.Mediator;
using Resrcify.SharedKernel.Mediator.Behaviors;
using Resrcify.SharedKernel.Mediator.Configuration;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Mediator.UnitTests.Configuration;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class StandardBehaviorsOptionsTests
{
    [Fact]
    public void BehaviorTypes_ShouldBeTheStandardChain_WhenNothingIsChanged()
        => new StandardBehaviorsOptions().BehaviorTypes().ShouldBe(
        [
            typeof(LoggingPipelineBehavior<,>),
            typeof(IdempotencyPipelineBehavior<,>),
            typeof(ValidationPipelineBehavior<,>),
            typeof(TransactionPipelineBehavior<,>),
            typeof(UnitOfWorkPipelineBehavior<,>),
            typeof(CachingPipelineBehavior<,>),
        ]);

    [Fact]
    public void BehaviorTypes_ShouldPutTheServicesBehaviorsInPlace_WhenInsertedAndOneIsLeftOut()
    {
        var options = new StandardBehaviorsOptions()
            .InsertAfter(StandardBehavior.Validation, typeof(FirstOwnBehavior<,>), typeof(SecondOwnBehavior<,>))
            .InsertBefore(StandardBehavior.Logging, typeof(OutermostBehavior<,>))
            .Without(StandardBehavior.Caching);

        options.BehaviorTypes().ShouldBe(
        [
            typeof(OutermostBehavior<,>),
            typeof(LoggingPipelineBehavior<,>),
            typeof(IdempotencyPipelineBehavior<,>),
            typeof(ValidationPipelineBehavior<,>),
            typeof(FirstOwnBehavior<,>),
            typeof(SecondOwnBehavior<,>),
            typeof(TransactionPipelineBehavior<,>),
            typeof(UnitOfWorkPipelineBehavior<,>),
        ]);
    }

    [Fact]
    public void BehaviorTypes_ShouldKeepAnInsertedBehavior_WhenItsAnchorIsLeftOut()
        => new StandardBehaviorsOptions()
            .Without(StandardBehavior.Caching)
            .InsertAfter(StandardBehavior.Caching, typeof(FirstOwnBehavior<,>))
            .BehaviorTypes()
            .Last()
            .ShouldBe(typeof(FirstOwnBehavior<,>));

    [Fact]
    public void InsertAfter_ShouldThrow_WhenTheTypeIsNotAnOpenBehavior()
        => Should.Throw<ArgumentException>(
            () => new StandardBehaviorsOptions().InsertAfter(StandardBehavior.Validation, typeof(string)));

    [Theory]
    [InlineData(typeof(NormalizingRequestBehavior<,>))]
    [InlineData(typeof(ValueTaskOnlyBehavior<,>))]
    public void InsertBefore_ShouldThrow_WhenTheBehaviorCantRunBetweenTheStandardOnes(Type behaviorType)
        => Should.Throw<ArgumentException>(
                () => new StandardBehaviorsOptions().InsertBefore(StandardBehavior.Validation, behaviorType))
            .Message.ShouldContain("isn't an IPipelineBehavior<,>");

    [Fact]
    public void Without_ShouldThrow_WhenTheBehaviorIsNotAStandardOne()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new StandardBehaviorsOptions().Without((StandardBehavior)42));

    private sealed class NormalizingRequestBehavior<TRequest, TResponse> : IRequestPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> Handle(TRequest request, RequestExecutionDelegate<TRequest, TResponse> next, CancellationToken cancellationToken)
            => next(request, cancellationToken);
    }

    private sealed class ValueTaskOnlyBehavior<TRequest, TResponse> : IValueTaskPipelineBehavior<TRequest, TResponse>
    {
        public ValueTask<TResponse> Handle(TRequest request, ValueTaskRequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class OutermostBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class FirstOwnBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
            => next(cancellationToken);
    }

    private sealed class SecondOwnBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
            => next(cancellationToken);
    }
}
