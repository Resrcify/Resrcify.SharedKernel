using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Resrcify.SharedKernel.Results.Diagnostics;
using Shouldly;
using Xunit;

namespace Resrcify.SharedKernel.Results.UnitTests.Diagnostics;

[SuppressMessage(
    "Performance",
    "CA1515:Consider making public types internal",
    Justification = "xUnit analyzer requires test classes to remain public for discovery in this project")]
public sealed class LoggedExceptionsTests
{
    [Fact]
    public void Claim_ShouldBeTrueOnlyTheFirstTime()
    {
        var exception = new InvalidOperationException("once");

        LoggedExceptions.Claim(exception).ShouldBeTrue();
        LoggedExceptions.Claim(exception).ShouldBeFalse();
        LoggedExceptions.Claim(exception).ShouldBeFalse();
    }

    [Fact]
    public void Claim_ShouldMarkTheExceptionUnderTheDocumentedKey()
    {
        var exception = new InvalidOperationException("marked");

        LoggedExceptions.Claim(exception);

        exception.Data[LoggedExceptions.DataKey].ShouldBe(true);
        LoggedExceptions.DataKey.ShouldBe("Resrcify.SharedKernel.Logged");
    }

    [Fact]
    public void Claim_ShouldBeFalse_WhenSomeoneMarkedItByTheKeyAlone()
    {
        // A package that doesn't reference this one follows the convention through the key.
        var exception = new InvalidOperationException("marked elsewhere");
        exception.Data["Resrcify.SharedKernel.Logged"] = true;

        LoggedExceptions.Claim(exception).ShouldBeFalse();
    }

    [Fact]
    public void Claim_ShouldAlwaysBeTrue_WhenTheExceptionsDataIsReadOnly()
    {
        var exception = Substitute.For<Exception>();
        exception.Data.Returns(new ReadOnlyDictionary<object, object>(new Dictionary<object, object>()));

        LoggedExceptions.Claim(exception).ShouldBeTrue();
        LoggedExceptions.Claim(exception).ShouldBeTrue();
        LoggedExceptions.IsLogged(exception).ShouldBeFalse();
    }

    [Fact]
    public void IsLogged_ShouldSayWhetherTheExceptionWasClaimed()
    {
        var exception = new InvalidOperationException("seen");

        LoggedExceptions.IsLogged(exception).ShouldBeFalse();
        LoggedExceptions.Claim(exception);
        LoggedExceptions.IsLogged(exception).ShouldBeTrue();
    }

    [Fact]
    public async Task Claim_ShouldBeTrueOnce_WhenManyClaimTheSameExceptionAtOnce()
    {
        // Many fresh exceptions: the race was on the first use of Exception.Data, which each claims at once.
        for (var round = 0; round < 200; round++)
        {
            var exception = new InvalidOperationException("contended");
            using var start = new ManualResetEventSlim();
            var claiming = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() =>
                {
                    start.Wait();
                    return LoggedExceptions.Claim(exception);
                }))
                .ToArray();
            start.Set();

            var claims = await Task.WhenAll(claiming);

            claims.Count(claimed => claimed).ShouldBe(1, $"round {round}");
        }
    }

    [Fact]
    public void Release_ShouldLetTheExceptionBeClaimedAgain_WhenItIsThrownAgainLater()
    {
        var exception = new InvalidOperationException("cached failure");
        LoggedExceptions.Claim(exception);

        LoggedExceptions.Release(exception);

        LoggedExceptions.IsLogged(exception).ShouldBeFalse();
        LoggedExceptions.Claim(exception).ShouldBeTrue();
        LoggedExceptions.Claim(exception).ShouldBeFalse();
    }

    [Fact]
    public void Claim_ShouldThrow_WhenTheExceptionIsNull()
        => Should.Throw<ArgumentNullException>(() => LoggedExceptions.Claim(null!));
}
