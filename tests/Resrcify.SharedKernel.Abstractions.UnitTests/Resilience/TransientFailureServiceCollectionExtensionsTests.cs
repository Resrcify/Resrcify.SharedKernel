using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.Results.Diagnostics;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Abstractions.UnitTests.Resilience;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class TransientFailureServiceCollectionExtensionsTests
{
    [Fact]
    public void TryAddTransientFailureClassifier_ShouldClassifyByTheBuiltInRule_WithoutRules()
    {
        var classifier = Classifier(services => services.TryAddTransientFailureClassifier());

        classifier.IsTransient(new TimeoutException("too slow")).ShouldBeTrue();
        classifier.IsTransient(new TransientFailureException("down")).ShouldBeTrue();
        classifier.IsTransient(new InvalidOperationException("a bug")).ShouldBeFalse();
    }

    [Fact]
    public void AddTransientFailureRule_ShouldMakeWhatTheRuleMatchesTransient()
    {
        var classifier = Classifier(services => services.AddTransientFailureRule<IOExceptionRule>());

        classifier.IsTransient(new IOException("the connection broke")).ShouldBeTrue();
        classifier.IsTransient(new InvalidOperationException("a bug")).ShouldBeFalse();
    }

    [Fact]
    public void AddTransientFailureRule_ShouldAskTheRuleAboutEveryCause()
    {
        var classifier = Classifier(services => services.AddTransientFailureRule<IOExceptionRule>());

        classifier.IsTransient(new InvalidOperationException("wrapped", new IOException("the connection broke"))).ShouldBeTrue();
        classifier.IsTransient(new AggregateException(new ArgumentException("no"), new IOException("broke"))).ShouldBeTrue();
    }

    [Fact]
    public void AddTransientFailureRule_ShouldTakeAFunction()
    {
        var classifier = Classifier(services => services.AddTransientFailureRule(
            exception => exception is NotSupportedException { Message: "not yet" }));

        classifier.IsTransient(new NotSupportedException("not yet")).ShouldBeTrue();
        classifier.IsTransient(new NotSupportedException("never")).ShouldBeFalse();
    }

    [Fact]
    public void AddTransientFailureRule_ShouldOnlyAddTransience()
    {
        // A rule saying "no" to everything doesn't make the built-in rule's transient failures permanent.
        var classifier = Classifier(services => services.AddTransientFailureRule(_ => false));

        classifier.IsTransient(new TimeoutException("too slow")).ShouldBeTrue();
    }

    [Fact]
    public void AddTransientFailureRule_ShouldOrEveryRule()
    {
        var classifier = Classifier(services => services
            .AddTransientFailureRule<IOExceptionRule>()
            .AddTransientFailureRule(exception => exception is NotSupportedException));

        classifier.IsTransient(new IOException("broke")).ShouldBeTrue();
        classifier.IsTransient(new NotSupportedException("not yet")).ShouldBeTrue();
        classifier.IsTransient(new ArgumentException("no")).ShouldBeFalse();
    }

    [Fact]
    public void AddTransientFailureRule_ShouldRegisterARuleTypeOnce_HoweverOftenItIsAdded()
    {
        var services = new ServiceCollection()
            .AddTransientFailureRule<IOExceptionRule>()
            .AddTransientFailureRule<IOExceptionRule>();

        services.Count(descriptor => descriptor.ServiceType == typeof(ITransientFailureRule)).ShouldBe(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(ITransientFailureClassifier)).ShouldBe(1);
    }

    [Fact]
    public void TryAddTransientFailureClassifier_ShouldKeepAClassifierTheServiceRegistered()
    {
        var own = new EverythingIsTransient();
        var services = new ServiceCollection()
            .AddSingleton<ITransientFailureClassifier>(own)
            .AddTransientFailureRule<IOExceptionRule>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITransientFailureClassifier>().ShouldBeSameAs(own);
    }

    private static ITransientFailureClassifier Classifier(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider().GetRequiredService<ITransientFailureClassifier>();
    }

    private sealed class IOExceptionRule
        : ITransientFailureRule
    {
        public bool IsTransient(Exception exception)
            => exception is IOException;
    }

    private sealed class EverythingIsTransient
        : ITransientFailureClassifier
    {
        public bool IsTransient(Exception exception)
            => true;
    }
}
