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

    [Fact]
    public void Without_ShouldThrow_WhenTheBehaviorIsNotAStandardOne()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => new StandardBehaviorsOptions().Without((StandardBehavior)42));

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
