using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Resrcify.SharedKernel.Abstractions.Resilience;
using Resrcify.SharedKernel.Results.Diagnostics;
using Resrcify.SharedKernel.UnitOfWork.Extensions;
using Resrcify.SharedKernel.UnitOfWork.Primitives;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Primitives;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class PersistenceTransientFailureRuleTests
{
    public static TheoryData<string, Exception, bool> Failures => new()
    {
        { "a serialization failure", DbExceptions.WithSqlState("40001"), true },
        { "a deadlock", DbExceptions.WithSqlState("40P01"), true },
        { "a concurrency conflict", new DbUpdateConcurrencyException("stale"), true },
        { "a deadlock under a save", new DbUpdateException("save failed", DbExceptions.WithSqlState("40P01")), true },
        { "a unique violation", DbExceptions.WithSqlState("23505"), false },
        { "a foreign-key violation", DbExceptions.WithSqlState("23503"), false },
        { "a handler's bug", new InvalidOperationException("broken"), false },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void IsTransient_ShouldTellASaveAnotherTryMayGetPast(string description, Exception failure, bool transient)
        => new PersistenceTransientFailureRule().IsTransient(failure).ShouldBe(transient, description);

    [Theory]
    [MemberData(nameof(Failures))]
    public void Classify_ShouldApplyTheRuleAndTheBuiltInOne_WithoutAClassifier(string description, Exception failure, bool transient)
        => PersistenceTransientFailureRule.Classify(failure, classifier: null).ShouldBe(transient, description);

    [Fact]
    public void Classify_ShouldApplyTheBuiltInRule_WithoutAClassifier()
    {
        PersistenceTransientFailureRule.Classify(new TimeoutException("too slow"), classifier: null).ShouldBeTrue();
        PersistenceTransientFailureRule.Classify(new TransientFailureException("down"), classifier: null).ShouldBeTrue();
    }

    [Fact]
    public void AddOutboxProcessing_ShouldRegisterTheRule_SoTheClassifierCallsAConcurrencyConflictTransient()
    {
        var services = new ServiceCollection();
        services.AddOutboxProcessing<TestDbContext>();
        using var provider = services.BuildServiceProvider();

        var classifier = provider.GetRequiredService<ITransientFailureClassifier>();

        classifier.IsTransient(new DbUpdateConcurrencyException("stale")).ShouldBeTrue();
        classifier.IsTransient(DbExceptions.WithSqlState("23505")).ShouldBeFalse();
    }
}
