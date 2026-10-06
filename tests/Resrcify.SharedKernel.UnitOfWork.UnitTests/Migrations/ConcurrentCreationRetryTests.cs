using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Storage;
using Resrcify.SharedKernel.UnitOfWork.Migrations;
using Resrcify.SharedKernel.UnitOfWork.UnitTests.Models;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.UnitOfWork.UnitTests.Migrations;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class ConcurrentCreationRetryTests
{
    [Theory]
    [InlineData("42P04")]
    [InlineData("42P06")]
    [InlineData("42P07")]
    [InlineData("42710")]
    [InlineData("23505")]
    public void IsConcurrentCreation_ShouldBeTrue_WhenTheDatabaseSaysItAlreadyExists(string sqlState)
    {
        var isRace = ConcurrentCreationRetry.IsConcurrentCreation(DbExceptions.WithSqlState(sqlState), out var found);

        isRace.ShouldBeTrue();
        found.ShouldBe(sqlState);
    }

    [Theory]
    [InlineData("42P01")]
    [InlineData("42601")]
    [InlineData("40001")]
    [InlineData("3D000")]
    [InlineData("28P01")]
    [InlineData(null)]
    public void IsConcurrentCreation_ShouldBeFalse_ForAnyOtherDatabaseError(string? sqlState)
    {
        var isRace = ConcurrentCreationRetry.IsConcurrentCreation(DbExceptions.WithSqlState(sqlState), out _);

        isRace.ShouldBeFalse();
    }

    [Fact]
    public void IsConcurrentCreation_ShouldReadTheDatabaseError_ThatAnExecutionStrategyGaveUpOn()
    {
        var exception = new RetryLimitExceededException("gave up", DbExceptions.WithSqlState("42P04"));

        ConcurrentCreationRetry.IsConcurrentCreation(exception, out var sqlState).ShouldBeTrue();
        sqlState.ShouldBe("42P04");
    }

    [Fact]
    public void IsConcurrentCreation_ShouldBeFalse_WhenNoDatabaseErrorIsBehindIt()
    {
        var exception = new InvalidOperationException("a migration's own bug", new TimeoutException());

        ConcurrentCreationRetry.IsConcurrentCreation(exception, out var sqlState).ShouldBeFalse();
        sqlState.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, 200)]
    [InlineData(2, 400)]
    [InlineData(3, 800)]
    [InlineData(4, 1_600)]
    public void DelayAfter_ShouldDoubleFrom200Milliseconds(
        int attempt,
        int milliseconds)
        => ConcurrentCreationRetry.DelayAfter(attempt).ShouldBe(TimeSpan.FromMilliseconds(milliseconds));
}
